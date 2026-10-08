# Frontend

React 19 + TypeScript + Vite, Mantine, TanStack Query, Zustand, dnd-kit. Описание проекта и запуск — в
[корневом README](../README.md).

| Команда | Что делает |
|---|---|
| `npm run dev` | dev-сервер на http://127.0.0.1:5173, `/api` проксируется на http://localhost:5080 (`API_URL`) |
| `npm run build` | проверка типов и сборка |
| `npm test` | Vitest |
| `npm run e2e` | Playwright (API замокан); `PW_CHANNEL=msedge` — взять системный браузер |
| `npm run gen:api` | перегенерировать `src/api/schema.d.ts` из OpenAPI запущенного API |
| `npm run lint` / `npm run format` | oxlint / Prettier |
