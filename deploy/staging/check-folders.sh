#!/bin/sh
# Refuses to let the beta stack start unless its media folders are beta-only (compose.yaml says why).
# Runs in the folder-check container with the two media folders mounted read-only at /check/tv-beta and /check/movies-beta.
fail() {
    echo "folder check FAILED: $*" >&2
    echo "Nothing was started. Fix .env (docs/setup-guide.md, the beta environment), then: docker compose up -d" >&2
    exit 1
}

trim() {
    echo "$1" | sed 's:/*$::'
}

tv=$(trim "$BETA_TV_FOLDER")
movies=$(trim "$BETA_MOVIES_FOLDER")
downloads=$(trim "$BETA_DOWNLOADS_FOLDER")

for folder in "$tv" "$movies"; do
    case "$(basename "$folder")" in
        *-beta) ;;
        *) fail "$folder: a beta media folder's name ends in -beta (tv-beta, movies-beta), so production's folders can't be used by mistake" ;;
    esac
    case "$folder/" in
        "$downloads"/*) fail "$folder is inside the downloads folder $downloads: mount only the downloads folder, never a folder that also holds media" ;;
    esac
    case "$downloads/" in
        "$folder"/*) fail "the downloads folder $downloads is inside $folder" ;;
    esac
done

[ "$tv" != "$movies" ] || fail "the TV and movie folders are the same folder ($tv)"

[ -f /check/tv-beta/.requestarr-beta ] || fail "$tv has no .requestarr-beta file. Create it once to confirm the folder is for beta only: touch \"$tv/.requestarr-beta\""
[ -f /check/movies-beta/.requestarr-beta ] || fail "$movies has no .requestarr-beta file. Create it once to confirm the folder is for beta only: touch \"$movies/.requestarr-beta\""

echo "folder check passed: TV $tv and movies $movies are beta-only; downloads $downloads"
