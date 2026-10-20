# Security policy

Debugging via MCP lets an AI agent drive the Visual Studio debugger, so security reports are taken seriously. How the
extension protects you (permission tiers, consent prompts, the local pipe) is described in
[docs/security.md](docs/security.md).

## Supported versions

This is a preview. Only the latest release receives fixes.

## Reporting a vulnerability

Please do not open a public issue for a vulnerability. Use GitHub's private reporting instead:
**Security → Report a vulnerability** on <https://github.com/msx752/DebugViaMcp/security/advisories/new>.

Include the Visual Studio version, the extension version, what you did and what happened. Reports about a way around a
consent prompt, a permission tier or the pipe's access control are the most valuable ones.

You can expect an answer within a few days. Out of scope: anything that needs a malicious process running as the same
Windows user (it can already control Visual Studio directly).
