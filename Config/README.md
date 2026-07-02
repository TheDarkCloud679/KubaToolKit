# Config

- `appsettings.json` and `loggroup-categories.json` are read by the app at
  startup and are versioned normally.
- `config` and `credentials` are **not** read by the app — they're a local
  reference copy of an AWS SSO setup (profiles, account IDs, SSO start URL)
  and are gitignored. KubaToolKit relies on the AWS SDK's standard
  `CredentialProfileStoreChain`, which reads from your machine's own
  `~/.aws/config` and `~/.aws/credentials` (or `%USERPROFILE%\.aws\` on
  Windows) — not from this folder.

To set up AWS profiles on a new machine:

1. Copy `Config/config` and `Config/credentials` (ask a teammate, or use
   them as a template) into `~/.aws/config` and `~/.aws/credentials`.
2. Run `aws sso login --sso-session kuba-sso` once, or just launch the app —
   it triggers SSO login automatically when a selected profile's token has
   expired (see `Shared/Services/AwsSsoService.cs`).
3. If you keep a local copy of `config`/`credentials` under `Config/` for
   your own reference, it stays untracked thanks to `.gitignore` — don't
   `git add -f` it.
