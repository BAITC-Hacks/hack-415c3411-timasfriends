# QuestBridge — TimasFriends

Unity 6000.3.7f1. Target: browser via WebGL.

## Run the first UI prototype

Open this repository as a Unity project. Open `Assets/QuestBridge/QuestBridge.unity` and press Play. The interface is created at runtime by `QuestBridgeApp`. No API keys are required.

Try scrolling the central catalog, selecting a round team avatar, opening a task, filtering categories, toggling focus and sound, and sending a draft. **Демо** starts a clearly labelled stream of sample leader changes every five seconds; **Пауза** stops it. Cards animate position and score changes; unchanged server snapshots keep their buttons and scroll position. Sound starts quietly enabled and remembers your mute preference. Focus uses a soft white veil, not a GPU blur; it suppresses sound and dims team details. The UI targets desktop landscape; WebGL build and browser verification are pending.

## Server integration

The FastAPI + Pydantic + SQLite backend is implemented in [`backend/`](backend/README.md). From the repository root (Python 3.12):

```powershell
py -3.12 -m venv backend/.venv
backend/.venv/Scripts/python.exe -m pip install -r backend/requirements.txt
backend/.venv/Scripts/python.exe -m uvicorn backend.app.main:app --host 127.0.0.1 --port 8000
```

Use `http://127.0.0.1:8000` as **Server Url** for local Unity Play mode. Health: `/health`; API documentation: `/docs`. No key is needed: chat and clarity explicitly report `aiMode: "fallback"`. The first start seeds five synthetic drafts, five published tasks, five teams and five proposals, all labelled demo. SQLite data persists in `backend/data/questbridge.sqlite3`.

Set **Server Url** on the QuestBridge object before entering Play (restart Play after changing it). Empty = offline demo. The client currently supports GET `/api/catalog` and POST `/api/chat`. Catalog request starts are scheduled five seconds apart; slow requests never overlap. Invalid snapshots are rejected before altering visible data. Chat keeps message history in the current session; input is locked during sending and preserved on error. AI fallback provides fixed questions and is explicitly labelled.

[API notes](docs/API.md) distinguish the current client integration from the implemented server endpoints. Publication, versioned edits, proposals, manual team decisions, milestones and events are available on the backend; their Unity controls and chat history across app restarts are subsequent slices. [The original backend specification](docs/BACKEND_AGENT_PROMPT.md) is retained for reference.

API keys belong only in server environment variables. [`backend/.env.example`](backend/.env.example) documents `OPENAI_API_KEY`, `AI_MODEL` and exact `CORS_ORIGINS`; both AI variables must be set to enable live AI. `.env` files are ignored. An HTTPS WebGL page requires HTTPS API and CORS configuration. Demo profile IDs are not account authentication; the explicitly named `demo-business` scope is only a local prototype convention.

## Rating and catalog rules

Implemented server formula: context 10, need 10, data 20, expected result 15, success criteria 15, constraints 10, users 10, contact 4, interaction format 3, feedback process 3. Only filled and confirmed fields count. Readiness stays 0–100; separate AI clarity 0–10 breaks ties, then stable id breaks remaining ties. This measures completeness, not truth. Low readiness does not block publication or proposals. Teams are always selected manually; a confirmed milestone earns 10 experience points once.

## Third-party

- Unity packages are listed in `Packages/manifest.json` and locked in `Packages/packages-lock.json`.
- Noto Sans Regular: https://github.com/notofonts/noto-fonts, SIL Open Font License; bundled license in `Assets/QuestBridge/Resources/OFL.txt`. Used for Cyrillic text.
- Kenney Interface Sounds (CC0): four small OGG clips for click, open, rise and leader feedback. [Source, license and file mapping](docs/THIRD_PARTY_AUDIO.md); original license bundled with audio.
- Backend: FastAPI 0.115.12, Pydantic 2.11.7, Uvicorn 0.34.3, HTTPX 0.28.1, python-dotenv 1.1.1; tests use pytest 8.4.1. [Backend Third-party notes](backend/README.md#third-party) include licenses and upstream links. SQLite is provided by Python's standard library.

## Verification

`AgentScripts/VerifyExperience.cs` runs through Unity Pipeline in Play mode. It checks stable buttons on repeated snapshots, retained scroll position, invalid response rejection, score ordering, avatar selection and loaded audio. Visual inspection is also performed in Game view. The backend API has been checked over local HTTP. Unity with the real backend and WebGL browser deployment have not yet been verified.

## Бизнес-задачи от Claude

[Бизнес-задачи AI Sana от Claude (DOCX)](docs/biznes-zadachi-AI-Sana.docx) — документ, предоставленный участником команды; источник указан с его слов. Исходный файл добавлен без изменений.
