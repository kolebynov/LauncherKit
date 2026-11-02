// Sends a picked file straight from the browser to R2 through presigned URLs (see S3PathField), so the file never
// passes through the editor's server or its Blazor circuit.

/**
 * Takes the file picked in `input` and clears the input, so picking the same file again raises another change.
 * Returns null when nothing is picked.
 */
export function openFile(input) {
	const file = input.files?.[0];
	input.value = "";
	return file ? createUpload(file) : null;
}

function createUpload(file) {
	const requests = new Set();
	let finishedBytes = 0;
	let hashedBytes = 0;
	let worker = null;
	let hash = null;
	let rejectHash = null;
	let aborted = false;

	return {
		info: () => ({ size: file.size, contentType: file.type }),

		/** Bytes sent so far, counting the parts in flight, and bytes hashed so far. */
		progress: () => {
			let uploaded = finishedBytes;
			for (const request of requests) {
				uploaded += request.loaded;
			}

			return { uploaded, hashed: hashedBytes };
		},

		/** Starts hashing the file on first call; resolves to its SHA-256 as uppercase hex. */
		hash: () => hash ??= new Promise((resolve, reject) => {
			if (aborted) {
				reject(new Error("Загрузка отменена."));
				return;
			}

			rejectHash = reject;
			worker = new Worker(new URL("./sha256-worker.js", import.meta.url));
			worker.onmessage = ({ data }) => {
				if (data.hashed !== undefined) {
					hashedBytes = data.hashed;
				} else {
					worker.terminate();
					data.error === undefined ? resolve(data.sha256) : reject(new Error(`Не удалось вычислить SHA-256: ${data.error}`));
				}
			};
			worker.onerror = e => {
				worker.terminate();
				reject(new Error(`Не удалось вычислить SHA-256: ${e.message}`));
			};
			worker.postMessage({ file, libraryUrl: new URL("../lib/hash-wasm/sha256.umd.min.js", import.meta.url).href });
		}),

		/**
		 * Sends `length` bytes from `offset` to the presigned `url`; `contentType` must be the type signed into the URL,
		 * or null when none was. Resolves to the ETag R2 returned, or null when the bucket's CORS rules do not expose it.
		 */
		uploadPart: (url, offset, length, contentType) => new Promise((resolve, reject) => {
			if (aborted) {
				reject(new Error("Загрузка отменена."));
				return;
			}

			const xhr = new XMLHttpRequest();
			const request = { xhr, loaded: 0 };
			requests.add(request);
			xhr.upload.onprogress = e => request.loaded = e.loaded;
			xhr.onload = () => {
				requests.delete(request);
				if (xhr.status >= 200 && xhr.status < 300) {
					finishedBytes += length;
					resolve(xhr.getResponseHeader("ETag"));
				} else {
					reject(new Error(`R2 ответил ${xhr.status}: ${errorMessage(xhr.responseText)}`));
				}
			};
			xhr.onerror = () => {
				requests.delete(request);
				reject(new Error("R2 недоступен или CORS-правила бакета не разрешают загрузку с этого адреса."));
			};
			xhr.onabort = () => {
				requests.delete(request);
				reject(new Error("Загрузка отменена."));
			};
			xhr.open("PUT", url);
			if (contentType !== null) {
				xhr.setRequestHeader("Content-Type", contentType);
			}

			xhr.send(file.slice(offset, offset + length));
		}),

		/** Stops every request in flight and the hashing; pending and later calls fail. */
		abort: () => {
			aborted = true;
			for (const request of requests) {
				request.xhr.abort();
			}

			worker?.terminate();
			// A terminated worker posts nothing more, so its pending hash would never settle.
			rejectHash?.(new Error("Загрузка отменена."));
		},
	};
}

/** The message of an S3 XML error response, or the response itself when it is not one. */
function errorMessage(responseText) {
	const message = /<Message>([^<]*)<\/Message>/.exec(responseText)?.[1];
	return message ?? (responseText.length > 200 ? `${responseText.slice(0, 200)}…` : responseText);
}
