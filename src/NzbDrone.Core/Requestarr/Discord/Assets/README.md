# Assets

These images are built into Requestarr (embedded resources) and sent with each Discord message that uses them, as attachments the message's embed points to (`attachment://name.png`, see `DiscordAssets`). Nothing is loaded from GitHub or any other host, so a self-hosted install works without internet access to this repository.

## tmdb-logo.png

The Movie Database (TMDB) approved "alt short" logo, from <https://www.themoviedb.org/about/logos-attribution> (`blue_short` SVG). It is rendered to PNG without changes to colour, aspect ratio or orientation, centred on a transparent square canvas, because Discord can't show SVG and draws footer icons as small circles.

Requestarr shows it, small, only while a TMDB key is set:

- next to "Cast from TMDB" in the footer of request cards that show TMDB data
- in the Credits topic of `/help` and `/admin help`

It is never more prominent than Requestarr's own branding.

Requestarr uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB. The logo is TMDB's trademark and is used under TMDB's API terms of use; it is not covered by this repository's GPL-3.0 licence.

## requestarr-avatar.png and requestarr-banner-*.png

The bot's own Discord picture (the plain R, the same in every environment) and banner (`teal` for production and anything that isn't a test environment, `dev`, `qa`, and `beta` for staging). Requestarr sets them on the bot when it starts, only when they differ, and never over a picture someone chose by hand (`BotProfileService`, setup guide 5.13). A new picture or banner reaches Discord with the release that contains it.

## feed-card-width.png

A fully transparent 800×1 image. Every card in the admin feed (`#requestarr-feed`) uses it as its embed image, because Discord sizes an embed to its content: with it, all cards are drawn the same width, whatever their text. It shows nothing.
