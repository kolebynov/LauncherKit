import { SELF } from "cloudflare:test";
import { describe, expect, it } from "vitest";

describe("installation analytics worker", () => {
	it("records a valid installation event", async () => {
		const response = await SELF.fetch(
			"https://example.com/v1/installations",
			{
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify({
					installationId: "319e470c-7d13-4ca7-a57f-c19afe90aacc",
					launcherVersion: "1.16.0",
				}),
			},
		);

		expect(response.status).toBe(204);
		expect(await response.text()).toBe("");
	});

	it("rejects an invalid installation identifier", async () => {
		const response = await SELF.fetch(
			"https://example.com/v1/installations",
			{
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify({ installationId: "not-a-guid" }),
			},
		);

		expect(response.status).toBe(400);
		expect(await response.json()).toEqual({
			error: "Invalid installation event",
		});
	});

	it("exposes a health endpoint", async () => {
		const response = await SELF.fetch("https://example.com/health");

		expect(response.status).toBe(200);
		expect(await response.json()).toEqual({ status: "ok" });
	});
});
