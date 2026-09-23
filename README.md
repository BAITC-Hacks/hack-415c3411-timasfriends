# QuestBridge — TimasFriends

Unity 6000.3.7f1. Target: browser via WebGL.

## Run the first UI prototype

Open this repository as a Unity project. Open `Assets/QuestBridge/QuestBridge.unity` and press Play. The interface is created at runtime by `QuestBridgeApp`. No API keys are required.

Try scrolling the central catalog, selecting a round team avatar, opening a task, filtering categories, toggling focus and sound, and sending a draft. **Демо** starts a clearly labelled stream of sample leader changes every five seconds; **Пауза** stops it. Cards animate position and score changes; unchanged server snapshots keep their buttons and scroll position. Sound starts quietly enabled and remembers your mute preference. Focus uses a soft white veil, not a GPU blur; it suppresses sound and dims team details. The UI targets desktop landscape; WebGL build and browser verification are pending.

## Server integration

Set **Server Url** on the QuestBridge object before entering Play (restart Play after changing it). Empty = offline demo. The client currently supports GET `/api/catalog` and POST `/api/chat`. Catalog request starts are scheduled five seconds apart; slow requests never overlap. Invalid snapshots are rejected before altering visible data. Chat keeps message history in the current session; input is locked during sending and preserved on error. AI fallback provides fixed questions and is explicitly labelled.

Send [the backend agent prompt](docs/BACKEND_AGENT_PROMPT.md) to the server developer. [API notes](docs/API.md) describe the initial client contract. Full publication, editable task forms, proposal actions, chat history across app restarts and milestone confirmation are subsequent slices, not implemented in this prototype.

API keys belong only in server environment variables. `.env.example` contains placeholders; `.env` files are ignored. An HTTPS WebGL page requires HTTPS API and CORS configuration.

## Rating and catalog rules

Proposed server formula: context 10, need 10, data 20, expected result 15, success criteria 15, constraints 10, users 10, contact 4, interaction format 3, feedback process 3. Only filled and confirmed fields count. Readiness stays 0–100; separate AI clarity 0–10 breaks ties, then stable id breaks remaining ties. Low readiness does not block publication or proposals. Teams are always selected manually.

## Third-party

- Unity packages are listed in `Packages/manifest.json` and locked in `Packages/packages-lock.json`.
- Noto Sans Regular: https://github.com/notofonts/noto-fonts, SIL Open Font License; bundled license in `Assets/QuestBridge/Resources/OFL.txt`. Used for Cyrillic text.
- Kenney Interface Sounds (CC0): four small OGG clips for click, open, rise and leader feedback. [Source, license and file mapping](docs/THIRD_PARTY_AUDIO.md); original license bundled with audio.

## Verification

`AgentScripts/VerifyExperience.cs` runs through Unity Pipeline in Play mode. It checks stable buttons on repeated snapshots, retained scroll position, invalid response rejection, score ordering, avatar selection and loaded audio. Visual inspection is also performed in Game view. A real backend and WebGL browser deployment are not yet verified.

## Бизнес-задачи от Claude

[Бизнес-задачи AI Sana от Claude (DOCX)](docs/biznes-zadachi-AI-Sana.docx) — документ, предоставленный участником команды; источник указан с его слов. Исходный файл добавлен без изменений.
