# Contributing

Issues and pull requests are welcome.

- **Bugs:** use the issue form. Include your Voice Flow and Windows versions. Never post keys, passwords or private audio.
- **Code:** run `npm ci`, `.\build.ps1`, `npm test` and `.\VoiceFlow.exe --self-test` before opening a pull request. Keep changes small and focused.
- **Scope:** the app stays simple: Ctrl+Space, speak, Ctrl+Space. New engines are fine if audio only goes to a service the user chose, with their own key.
- **Not accepted:** anything that reads another app's sign-in or credentials, or sends audio anywhere the user did not choose.
