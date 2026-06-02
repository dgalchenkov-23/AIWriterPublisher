import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // Явный IPv4: на Windows localhost иногда только [::1], браузер идёт на 127.0.0.1
    host: '127.0.0.1',
    port: 5174,
    strictPort: false,
    open: true,
  },
  preview: {
    host: '127.0.0.1',
    port: 4173,
  },
});
