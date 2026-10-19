# Research & Legal Notice

**This repository is a research artifact. It exists to study interoperability, document observed
behaviour, and provide a clean-room reimplementation example. It is not a Microsoft product, is not
affiliated with or endorsed by Microsoft, and is not intended to be a drop-in replacement for
Windows Task Manager.**

## What this project is

- A **clean-room reconstruction** of the user-facing behaviour of Windows 11 Task Manager
  (`Taskmgr.exe`, build 26200), written from scratch in C#/WPF.
- A **reverse-engineering study** of how the shipped product is built: PE metadata, imported
  Windows APIs, MUI resource strings, UI Automation structure, and observable behaviour.
- Evidence is kept in `REPORT.md`, `REA-EVIDENCE.md`, `evidence/`, `recon/` and `tools/`.

## What this project is not

- It does **not** contain, link against, or redistribute any Microsoft binary
  (`Taskmgr.exe`, DLLs, MUI files, icons or other assets).
- It does **not** contain Microsoft source code, decompiled code, or disassembly listings.
- It is **not** affiliated with, sponsored by, or approved by Microsoft Corporation.
- It is **not** intended for malware, evasion, or bypassing security controls.

Screenshots of the shipping product and interface labels extracted from its resources appear only
for identification, commentary and interoperability discussion. All rights in those materials remain
with Microsoft Corporation. "Windows", "Task Manager" and related marks are trademarks of Microsoft.

The embedded application icon (`src/TaskMgrClone/Assets/TaskMgrClone.ico`) is original artwork created
for this project. At runtime the app may read the icon of the locally installed `Taskmgr.exe` to
display it in its own title bar; that icon is read from the user's own Windows installation and is
never copied into this repository or redistributed by it.

## Legal basis and caution

Reverse engineering for interoperability and research is expressly recognised in several
jurisdictions, for example:

- **United States** — 17 U.S.C. § 1201(f) (reverse engineering for interoperability) and the fair-use
  doctrine for research, commentary and criticism.
- **European Union** — Directive 2009/24/EC, Article 6 (decompilation for interoperability).

Nothing in this repository is legal advice. If you intend to use or redistribute this material,
consult a qualified lawyer in your jurisdiction and respect the rights of the original vendor.

## No warranty

The software and documentation are provided **"as is"**, without warranty of any kind, express or
implied, including but not limited to the warranties of merchantability, fitness for a particular
purpose and non-infringement. The authors are not liable for any claim, damages or other liability
arising from the use of this material. Some operations exercised by the app (ending processes,
controlling services, changing power throttling) affect the operating system — use it at your own
risk.

## Takedown requests

If you are a rights holder and believe content in this repository infringes your rights, please open
an issue in this repository or contact the maintainer through GitHub. Content will be reviewed and,
where appropriate, removed promptly.