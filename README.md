# QuestBridge — TimasFriends

Unity 6000.3.7f1. Target: browser via WebGL.

## Run the first UI prototype

Open this repository as a Unity project. Open `Assets/QuestBridge/QuestBridge.unity` and press Play. The interface is created at runtime by `QuestBridgeApp`. No API keys are required.

Try scrolling the central catalog, selecting the team initials on a card, toggling focus and sound, sending a draft, and pressing **Демо: новый лидер**. Demo data and manual events are labelled as demo. Sound defaults to off. Focus currently uses a white veil, not a GPU blur. The first slice is desktop landscape UI; WebGL build and browser verification are pending.

## Server integration

Set **Server Url** on the QuestBridge object before entering Play. Empty = offline demo. The client currently supports GET `/api/catalog` and POST `/api/chat`. It polls the catalog sequentially with five seconds between requests, animates changed positions and keeps the latest data on connection failure. AI fallback provides fixed questions and is explicitly labelled.

Send [the backend agent prompt](docs/BACKEND_AGENT_PROMPT.md) to the server developer. [API notes](docs/API.md) describe the initial client contract. Full publication, editable task forms, proposal actions, persistent chat history UI and milestone confirmation are subsequent slices, not implemented in this prototype.

API keys belong only in server environment variables. `.env.example` contains placeholders; `.env` files are ignored. An HTTPS WebGL page requires HTTPS API and CORS configuration.

## Rating and catalog rules

Proposed server formula: context 10, need 10, data 20, expected result 15, success criteria 15, constraints 10, users 10, contact 4, interaction format 3, feedback process 3. Only filled and confirmed fields count. Readiness stays 0–100; separate AI clarity 0–10 breaks ties, then stable id breaks remaining ties. Low readiness does not block publication or proposals. Teams are always selected manually.

## Third-party

- Unity packages are listed in `Packages/manifest.json` and locked in `Packages/packages-lock.json`.
- Noto Sans Regular: https://github.com/notofonts/noto-fonts, SIL Open Font License; bundled license in `Assets/QuestBridge/Resources/OFL.txt`. Used for Cyrillic text.
- UI sounds are generated mathematically by our code; no external audio assets.
