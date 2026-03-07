import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5000,
    proxy: {
      '/api': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
      // Service-specific hub proxies — must precede the generic /hubs rule
      '/hubs/marketdata': {
        target: 'http://localhost:5002',
        ws: true,
        changeOrigin: true,
      },
      '/hubs/execution': {
        target: 'http://localhost:5004',
        ws: true,
        changeOrigin: true,
      },
      '/hubs/agent-runner': {
        target: 'http://localhost:5003',
        ws: true,
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:5001',
        ws: true,
      },
    },
  },
});
