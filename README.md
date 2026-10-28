<p align="center"><img src="assets/banner.png" alt="Voice Flow: Bangla and English voice typing for Windows" width="720"></p>

# Voice Flow

Bangla and English voice typing for Windows. Press **Ctrl+Space**, speak, press **Ctrl+Space** again. The text appears where your cursor is.

বাংলা ও English মিশিয়ে কথা বলুন, লেখা কার্সরের জায়গায় বসে যাবে।

<p align="center"><img src="docs/screenshots/home.png" alt="Voice Flow main window" width="520"> <img src="docs/screenshots/key.png" alt="Paste your engine key" width="300"></p>

## Install

1. Download `VoiceFlow-0.3.3-Setup.exe` from the [Releases](../../releases) page and run it. Windows 10 or 11 (x64) with .NET Framework 4.7.2 or newer. Node.js is included.
2. Open Voice Flow and pick a voice engine (below).
3. Put the cursor where you want to type. **Ctrl+Space**, speak, **Ctrl+Space**. **Esc** cancels.

The installer is not code-signed yet, so Windows may warn you. Check the SHA-256 in `SHA256SUMS.txt` against the file you downloaded.

## Voice engines

| Engine | Internet | Cost | Setup |
|---|---|---|---|
| **Local model** | Once, to download (about 71 MB). Then none. | Free | Press "লোকাল মডেল নামান" |
| **Sarvam** | Yes | Your own Sarvam account | "চাবি নিন" opens their key page. Create a key, copy it, press "চাবি বসান", paste |
| **Chariot** | Yes | Your own Chariot account | Same as Sarvam |

The app shows these steps for the engine you pick. Audio goes only to the engine you chose. With the local model it never leaves your computer. Details: [PRIVACY.md](PRIVACY.md).

If Sarvam or Chariot fails and the local model is installed, the same recording is read again by the local model and the caption tells you.

## What it does

- A small caption card shows the text while you speak. It does not take focus.
- If pasting is not possible, the text stays on the clipboard.
- **সাম্প্রতিক কথা** keeps your last 20 recordings (text and audio), encrypted for your Windows user. You can play, copy, retry or delete them.
- The window's × button keeps the app in the tray. Use the tray menu to quit.

Accuracy depends on the engine, your microphone and your speech. No accuracy or cost guarantees are made here.

## Report a problem

"···" menu, then "সমস্যা জানান" opens the GitHub issue page. You write the report yourself. Do not post passwords, keys or private audio. [TROUBLESHOOTING.md](TROUBLESHOOTING.md)

## Build from source

Needs the .NET Framework compiler that ships with Windows, and Node.js 24 or newer.

```powershell
npm install
.\build.ps1                 # builds VoiceFlow.exe
npm test                    # bridge tests
.\VoiceFlow.exe --self-test # native checks in an isolated data folder
.\package-release.ps1       # builds dist\VoiceFlow-0.3.3-Setup.exe
```

## License

[MIT](LICENSE). Third-party components and the downloaded model have their own licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
