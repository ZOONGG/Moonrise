# GitHub repository setup

Use these settings for `ZOONGG/Moonrise`.

## General

Set the repository description to:

> Open-source Windows mod manager and launcher for Lunar Client with local Weave mods, Java agents and safe diagnostics.

Keep **Website** empty until a real project site or documentation URL exists.

Under **Social preview**, upload `docs/images/moonrise-social-preview.png`.

Under **Social preview / Features**, ensure **Releases** is visible on the repository home page. Leave **Packages** disabled unless the project begins publishing a useful package.

## Topics

Replace irrelevant or duplicate topics with:

```text
minecraft
minecraft-189
minecraft-mods
lunar-client
mod-manager
launcher
java-agent
weave-loader
windows
windows-desktop
wpf
dotnet
csharp
open-source
```

## Branch protection

Open **Settings → Branches → Add branch protection rule** for `main`:

- require a pull request before merging;
- require the `build` status check to pass;
- require branches to be up to date before merging;
- require conversation resolution;
- prevent force pushes and branch deletion;
- apply the rule to administrators when appropriate for the maintainer workflow.

## Actions permissions

Open **Settings → Actions → General**:

- allow only actions required by the repository, or allow listed actions and reusable workflows;
- grant workflow `GITHUB_TOKEN` **Read repository contents** by default;
- allow the tagged release workflow to receive `contents: write` through its explicit workflow permission;
- do not add repository secrets unless a documented release feature requires one.

The current workflows need no custom secrets and do not publish packages.
