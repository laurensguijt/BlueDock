# Code signing

The installer and the portable build are unsigned, so Windows SmartScreen warns the first time
anyone runs them. That warning is the biggest obstacle to someone other than the author
installing BlueDock, and it does not go away by itself: reputation is tied to a certificate,
and without one every new release starts from zero.

A certificate normally costs a few hundred euro a year. [SignPath
Foundation](https://signpath.org/) issues them free of charge to open source projects, which is
the route worth trying here.

## What SignPath asks for

Checked against <https://signpath.org/terms> (that page is marked as a draft, so verify before
applying — the conditions may have moved on).

Already true of BlueDock:

- An OSI approved licence with no commercial dual licensing. MIT qualifies.
- No proprietary components. Everything here is this repository plus the .NET runtime.
- Already released in the form to be signed, which the GitHub releases are.
- Functionality described on the download page, which the README does.
- An uninstaller, which the Inno Setup installer provides.
- Built from source in a verifiable way: the release workflow builds and packages everything,
  and the source of both is in this repository.

Still to arrange:

- **A privacy statement.** Software that contacts a system the user did not choose has to say
  so. BlueDock makes exactly one such call — the update check against the GitHub releases API —
  and it is described under *Privacy* in the README and can be switched off.
- **A code signing policy on the project home page.** SignPath wants a section using that exact
  term, naming the team roles and carrying the sentence *"Free code signing provided by
  SignPath.io, certificate by SignPath Foundation"*. Add it to the README once accepted.
- **Multi factor authentication** on both the GitHub account and the SignPath account.
- **Team roles.** Authors, Reviewers and Approvers. For a one person project this means
  declaring that the single maintainer holds all three, and that every signing request is
  approved by hand.
- **Fixed artifact metadata.** Product name and product version have to be set and consistent
  per build. `BlueDock.csproj` sets `Product` and takes `Version` from the release tag, so this
  is close, but the exact enforcement rules are configured on the SignPath side.

## Applying

The form is at <https://signpath.org/apply>. Its contents are rendered by JavaScript and could
not be read ahead of time, so what it asks for is unknown until you open it. Acceptance is at
SignPath's discretion and every release needs manual approval before it is signed; no
turnaround time is published.

## Wiring it into the release

Not done, deliberately. SignPath publishes a GitHub Action for submitting signing requests, but
its identifier and inputs could not be confirmed from the pages above, and an untested signing
step in `build.yml` would be dead weight until the application is accepted. Take the current
instructions from SignPath's own documentation at <https://about.signpath.io> at that point, and
add the step between building the installer and publishing the release.

## If SignPath declines

Azure Trusted Signing is the cheap commercial alternative, in the order of ten euro a month,
and is available to individuals who can prove three years of trading history. A plain OV
certificate from a reseller also works but builds reputation slowly. Neither is worth doing
before the free route has been tried.
