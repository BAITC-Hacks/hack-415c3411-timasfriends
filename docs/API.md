# API integration

Canonical proposed contract and server-agent task: [BACKEND_AGENT_PROMPT.md](BACKEND_AGENT_PROMPT.md).

The first Unity slice currently calls only:
- GET `{serverUrl}/api/catalog`: `{cards:[{id,title,description,category,readiness,clarity,teamIds:[]}],teams:[{id,name,initials,stack,description,completed}]}`.
- POST `{serverUrl}/api/chat`: `{conversationId,message}` -> `{conversationId,message}` (additional fields allowed).

Empty `serverUrl` means offline demo. Catalog polling is sequential, minimum five seconds between completed requests. Timeout retains last snapshot. Responses must use stable unique card ids. Client sorts readiness descending, clarity descending, id ascending.

API key belongs exclusively to server environment. Configure CORS for the WebGL host; serve API over HTTPS for an HTTPS-hosted client. Do not deploy a localhost URL for remote users.
