# QA checklist

What admins on the QA server test, in Discord, before a version goes to staging. Requestarr posts these items in the QA checklist channel for every version QA has that staging doesn't; admins press Passes or Failing on each. When every item has passed and no bug report from QA is still open, the version can go to staging.

Every pull request adds its version's items here, the way it adds its changelog entry: each one says what to do and what you should see, in plain words. A version with nothing anyone can test by hand has only its heading. See docs/development.md#qa-checklist.

## 0.5.1 (2026-10-27)

## 0.5.0 (2026-10-26)
- **Version on the logo:** In the web app, point at the logo at the top left and wait a moment. You should see "Requestarr 0.5.0".
- **Version in Status:** In the web app, open System, then Status. You should see version 0.5.0 followed by its build number, with no "beta" in it.

## 0.4.17 (2026-10-26)

## 0.4.16 (2026-10-26)
- **Passes without errors:** Press Passes on a checklist item, then press it again. You should see the card show who passed it and when, and no "Something went wrong" message.
- **Failing in a row:** Press Failing on three items one after another and send a short report for each. You should see a form open every time, each card marked failing with its own report, and never "You've sent a report just now".
- **Member limit still holds:** In the bug reports channel, press Report a problem and send a report, then press it again straight away. You should see Requestarr ask you to wait.
- **Bug reports channel order:** Open the bug reports channel after sending a report, on your phone and on your computer. You should see the reports in the order they were sent, the Report a problem message last, and no reply pointing at a deleted message.
- **Why a report isn't on GitHub:** Look at the card of a report you sent. You should see either the link to its GitHub issue, or the exact reason it isn't there yet, such as the server having no GitHub access key.
- **QA group:** Look at the server's channel list. You should see the QA checklist channel in its own Requestarr QA group with all its cards, and a member who isn't an admin shouldn't see that group.

## 0.4.15 (2026-10-25)
- **Test checklist:** Open the QA checklist channel in the Requestarr QA group. You should see a card for each thing to test in the versions QA has that staging doesn't, each with Passes and Failing buttons, and a summary card at the top.
- **Passing an item:** Press Passes on one card. You should see the card say who passed it and when, and the summary count go up by one.
- **Failing an item:** Press Failing on another card, pick the part of Requestarr, describe what went wrong and send it. You should see the card marked failing with a link to its bug report, and the summary say the checklist can't complete while it fails.
- **Report a problem:** In the bug reports channel in the Requestarr Users group, press Report a problem, pick a part, read the note that reports are public, and send a short test report. You should see a card for your report in the channel, and the link to its issue on GitHub once it's filed.
- **Report limit:** Send several reports one after another. You should see Requestarr ask you to wait once you reach the limit, and no more cards posted until then.

## 0.4.14 (2026-10-25)

## 0.4.13 (2026-10-25)
- **Sign-in logo:** Sign out of the web app and look at the sign-in page in the light theme, then in the dark theme. You should see the logo with a light arrow, easy to read on the gray box, in both.
- **Loading screen:** Reload the web app and watch it while it starts. You should see the QA logo, the same one as the header's, with no purple square.

## 0.4.12 (2026-10-25)
- **Bot picture and banner:** In the Requestarr QA server, click the bot's name to open its profile. You should see the plain Requestarr picture and the yellow QA banner.

## 0.4.11 (2026-10-25)
- **Logo on the purple header:** Switch the web app to the light theme and look at the header. You should see the QA bar and the R straight on the purple, with no white box around them.
- **Charcoal arrow:** In the light theme, look at the header logo and the icon in the browser tab. You should see the arrow in a soft charcoal instead of solid black.

## 0.4.10 (2026-10-25)
- **Dark theme icons:** Switch the web app to the dark theme and look at the header and the browser tab. You should see the logo and the icon on charcoal gray, never on black.
- **Full-height logo:** Look at the logo at the top left of the header. You should see the yellow QA bar beside a full-height R, not a small square.

## 0.4.9 (2026-10-25)

## 0.4.8 (2026-10-25)

## 0.4.7 (2026-10-25)

## 0.4.6 (2026-10-25)

## 0.4.5 (2026-10-25)

## 0.4.4 (2026-10-25)

## 0.4.3 (2026-10-25)
