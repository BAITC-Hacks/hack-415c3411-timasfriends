# API integration

Canonical proposed contract and server-agent task: [BACKEND_AGENT_PROMPT.md](BACKEND_AGENT_PROMPT.md).

The first Unity slice currently calls only:
- GET `{serverUrl}/api/catalog`: `{cards:[{id,title,description,category,readiness,clarity,teamIds:[]}],teams:[{id,name,initials,stack,description,completed}]}`.
- POST `{serverUrl}/api/chat`: `{conversationId,message}` -> `{conversationId,message}` (additional fields allowed).

Empty `serverUrl` means offline demo. Set it before entering Play and restart Play when switching servers. Catalog polling is sequential, with requests scheduled approximately five seconds apart and no overlap on slow responses. Timeout retains the last snapshot. Both card and team ids must be unique and nonempty; teamIds must resolve to teams in the same snapshot. Readiness is 0–100; clarity is finite 0–10. The client validates before applying and sorts readiness descending, clarity descending, id ascending.

Chat responses require nonempty `conversationId` and `message`. Optional `aiMode: "fallback"` changes the assistant status. Draft/phase/publication integration remains a later slice. Input is locked during a request; errors preserve the submitted text. Chat history is displayed for the current app session.

API key belongs exclusively to server environment. Configure CORS for the WebGL host; serve API over HTTPS for an HTTPS-hosted client. Do not deploy a localhost URL for remote users.
