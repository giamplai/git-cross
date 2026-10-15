# Security policy

## Supported versions

Security fixes are made for the latest release only.

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Use GitHub's private reporting: on the repository page choose **Security → Report a vulnerability**
(<https://github.com/giamplai/git-cross/security/advisories/new>). Include what you found, how to reproduce it
and the version (Settings dialog, bottom left). You can expect an acknowledgement within a few days and a fix or a
mitigation as soon as it can be prepared; you will be credited in the release notes unless you prefer otherwise.

## What matters most for this application

* The app runs the `git` executable and optional external merge/diff tools **without a shell**; reports of ways to make a
  repository name, branch name, file name or configuration value run unintended commands are in scope.
* Handling of untrusted repositories (hooks are executed by git itself, as in the command line).
* Anything that could delete or overwrite user data without the confirmation described in the user guide.

Out of scope: vulnerabilities in git itself, in third-party tools you configured, or that require an already-compromised machine.
