const INSTALLATION_PATH = "/v1/installations";
const MAX_REQUEST_BYTES = 1_024;
const MAX_VERSION_LENGTH = 64;
const UUID_PATTERN =
	/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

interface InstallationEvent {
	installationId: string;
	launcherVersion?: string;
}

interface WorkerEnv {
	ANALYTICS: AnalyticsEngineDataset;
	INSTALLATION_ID_HASH_KEY: string;
}

export default {
	async fetch(request, env): Promise<Response> {
		const url = new URL(request.url);

		if (request.method === "GET" && url.pathname === "/health") {
			return Response.json({ status: "ok" });
		}

		if (url.pathname !== INSTALLATION_PATH) {
			return jsonError(404, "Not found");
		}

		if (request.method !== "POST") {
			return new Response(null, {
				status: 405,
				headers: { Allow: "POST" },
			});
		}

		const contentLength = Number(request.headers.get("content-length"));
		if (Number.isFinite(contentLength) && contentLength > MAX_REQUEST_BYTES) {
			return jsonError(413, "Request body is too large");
		}

		if (!request.headers.get("content-type")?.startsWith("application/json")) {
			return jsonError(415, "Content-Type must be application/json");
		}

		let event: unknown;
		try {
			const body = await request.text();
			if (new TextEncoder().encode(body).byteLength > MAX_REQUEST_BYTES) {
				return jsonError(413, "Request body is too large");
			}

			event = JSON.parse(body);
		} catch {
			return jsonError(400, "Request body must be valid JSON");
		}

		if (!isInstallationEvent(event)) {
			return jsonError(400, "Invalid installation event");
		}

		const installationKey = await createInstallationKey(
			event.installationId,
			env.INSTALLATION_ID_HASH_KEY,
		);

		env.ANALYTICS.writeDataPoint({
			indexes: [installationKey],
			blobs: [event.launcherVersion ?? "unknown"],
		});

		return new Response(null, { status: 204 });
	},
} satisfies ExportedHandler<WorkerEnv>;

function isInstallationEvent(value: unknown): value is InstallationEvent {
	if (typeof value !== "object" || value === null) {
		return false;
	}

	const event = value as Record<string, unknown>;
	if (
		typeof event.installationId !== "string" ||
		!UUID_PATTERN.test(event.installationId)
	) {
		return false;
	}

	return (
		event.launcherVersion === undefined ||
		(typeof event.launcherVersion === "string" &&
			event.launcherVersion.length > 0 &&
			event.launcherVersion.length <= MAX_VERSION_LENGTH)
	);
}

async function createInstallationKey(
	installationId: string,
	secret: string,
): Promise<string> {
	const encoder = new TextEncoder();
	const key = await crypto.subtle.importKey(
		"raw",
		encoder.encode(secret),
		{ name: "HMAC", hash: "SHA-256" },
		false,
		["sign"],
	);
	const signature = await crypto.subtle.sign(
		"HMAC",
		key,
		encoder.encode(installationId.toLowerCase()),
	);

	return bytesToHex(new Uint8Array(signature));
}

function bytesToHex(bytes: Uint8Array): string {
	let result = "";
	for (const byte of bytes) {
		result += byte.toString(16).padStart(2, "0");
	}

	return result;
}

function jsonError(status: number, error: string): Response {
	return Response.json({ error }, { status });
}
