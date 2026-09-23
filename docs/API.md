# API integration

Original specification: [BACKEND_AGENT_PROMPT.md](BACKEND_AGENT_PROMPT.md). The backend is implemented in [`backend/`](../backend/README.md); the Unity integration below remains limited to two endpoints.

The first Unity slice currently calls only:
- GET `{serverUrl}/api/catalog`: `{cards:[{id,title,description,category,readiness,clarity,teamIds:[]}],teams:[{id,name,initials,stack,description,completed}]}`.
- POST `{serverUrl}/api/chat`: `{conversationId,message}` -> `{conversationId,message}` (additional fields allowed).

Empty `serverUrl` means offline demo. Set it before entering Play and restart Play when switching servers. Catalog polling is sequential, with requests scheduled approximately five seconds apart and no overlap on slow responses. Timeout retains the last snapshot. Both card and team ids must be unique and nonempty; teamIds must resolve to teams in the same snapshot. Readiness is 0–100; clarity is finite 0–10. The client validates before applying and sorts readiness descending, clarity descending, id ascending.

Chat responses require nonempty `conversationId` and `message`. Optional `aiMode: "fallback"` changes the assistant status. Draft/phase/publication integration remains a later slice. Input is locked during a request; errors preserve the submitted text. Chat history is displayed for the current app session.

API key belongs exclusively to server environment. Configure CORS for the WebGL host; serve API over HTTPS for an HTTPS-hosted client. Do not deploy a localhost URL for remote users.

## Local connection

Base URL: `http://127.0.0.1:8000`. Follow [server startup](../backend/README.md), then set **QuestBridgeApp → Server Url**. GET `/health` returns `{"status":"ok","service":"questbridge","aiMode":"fallback","demo":true}` when AI is not configured. GET `/api/catalog` includes a numeric `version`; team profiles additionally expose numeric `experience`.

POST `/api/chat` example: `{"conversationId":"","message":"Учителя долго проверяют пробные SAT"}`. A short initial description gets three questions. Reuse its returned `conversationId` with `{"conversationId":"<returned id>","message":"Не знаю"}` to obtain `phase:"draft_ready"` with empty unknown fields. An unknown nonempty conversation ID returns 404. The server stores conversation history; Unity sends only the next message. Additional response fields include `phase`, `aiMode`, `questions`, `draft`, `sources` and `missingFields`.

## Implemented server endpoints for the next Unity slice

- POST `/api/tasks/preview`: deterministic readiness and ten score rows.
- POST `/api/tasks`: explicit `confirmed:true`, required nonempty title/category, optional `Idempotency-Key` header.
- GET/PATCH `/api/tasks/{id}`: complete card; PATCH requires `version` and the complete `draft`/`confirmedFields`. Include changed confirmed fields in `reconfirmedFields` or remove them from `confirmedFields`. Stale versions and missing fresh confirmations return 409.
- POST/GET `/api/tasks/{id}/proposals`: one proposal per team/task; no readiness threshold. Public details are `null` unless `publishDetails:true`. For the **local demo only**, `?scope=demo-business&businessId=business-demo` exposes the owner's view. This is not authentication or a privacy boundary.
- PATCH `/api/proposals/{id}/decision`: `{businessId,decision:"selected"|"rejected"}`. Multiple selections are allowed.
- POST `/api/proposals/{id}/milestones`: `{teamId,description}` for a selected team.
- POST `/api/milestones/{id}/confirm`: `{businessId}`; one transaction awards 10 experience points and one completed stage, once per milestone.
- GET `/api/events?after=0&limit=100`: real persisted events, at most 200 per page; the cursor advances only to the last returned event.

These endpoints are implemented and can be exercised with `backend/examples/demo.py`; current Unity UI does not yet invoke them. Exact schemas, limits and errors are in `/openapi.json` and [backend documentation](../backend/README.md). All API errors use `{"error":{"code":"...","message":"..."}}`.
