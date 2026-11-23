import {defineConfig, loadEnv} from "vite";

export default defineConfig(({mode}) => {
  const env = loadEnv(mode, ".", "PORTAL_");
  return {
    server: {
      proxy: {
        "/api": {target: env.PORTAL_PROXY_ALVO || "https://localhost:7143", secure: false}
      }
    }
  };
});
