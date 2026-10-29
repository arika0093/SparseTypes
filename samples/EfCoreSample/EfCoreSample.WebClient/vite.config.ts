import react from "@vitejs/plugin-react";
import { defineConfig, loadEnv } from "vite";

export default defineConfig(({ mode }) => {
  const { API_URL = "http://localhost:5000" } = loadEnv(mode, process.cwd(), "");
  const proxy = { target: API_URL, changeOrigin: true };

  return {
    plugins: [react()],
    server: {
      host: "0.0.0.0",
      proxy: {
        "/orders": proxy,
      },
    },
  };
});
