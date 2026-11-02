// Hashes the posted File with SHA-256 off the page's thread. Web Crypto only hashes a whole buffer at once, which a
// multi-gigabyte file does not fit in, so the file is read in chunks and hashed incrementally with hash-wasm.
// Receives { file, libraryUrl } (the absolute URL of hash-wasm's SHA-256 build, resolved by the page because a worker
// may run from a blob: URL that relative URLs cannot be resolved against).
// Posts { hashed } after each chunk, then { sha256 } (uppercase hex, the form of feed download hashes) or { error }.
"use strict";

const ChunkSize = 8 * 1024 * 1024;

async function hashFile(file) {
	const sha256 = await hashwasm.createSHA256();
	const read = offset => file.slice(offset, offset + ChunkSize).arrayBuffer();
	// The next chunk is read while the current one is hashed.
	let next = read(0);
	for (let offset = 0; offset < file.size; offset += ChunkSize) {
		const chunk = new Uint8Array(await next);
		next = offset + ChunkSize < file.size ? read(offset + ChunkSize) : null;
		sha256.update(chunk);
		postMessage({ hashed: offset + chunk.length });
	}

	return sha256.digest("hex").toUpperCase();
}

onmessage = async ({ data: { file, libraryUrl } }) => {
	try {
		importScripts(libraryUrl);
		postMessage({ sha256: await hashFile(file) });
	} catch (e) {
		postMessage({ error: e instanceof Error ? e.message : String(e) });
	}
};
