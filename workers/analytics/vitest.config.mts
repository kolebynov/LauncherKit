import { cloudflareTest } from "@cloudflare/vitest-plugin";
import { defineConfig } from "vitest/config";

export default defineConfig({
	plugins: [
		cloudflareTest({
			wrangler: { configPath: "./test/wrangler.jsonc" },
			miniflare: {
				bindings: {
					INSTALLATION_ID_HASH_KEY: "local-test-key",
				},
			},
		}),
	],
});
