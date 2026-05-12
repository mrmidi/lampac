#!/usr/bin/env bash
# TvClient v1 — end-to-end smoke tests
# Usage:
#   ./Tests/tvclient-e2e.sh                              # default: https://lamp.mrmidi.net:8443
#   BASE=https://myhost:9118 ./Tests/tvclient-e2e.sh
#   ./Tests/tvclient-e2e.sh --verbose                    # print full JSON on failures

set -euo pipefail

VERBOSE=false
POSITIONAL=()
for arg in "$@"; do
    case "$arg" in
        --verbose) VERBOSE=true ;;
        http*) BASE="$arg" ;;
        *) POSITIONAL+=("$arg") ;;
    esac
done

if [[ -z "${BASE:-}" ]]; then
    echo "Usage: BASE=https://yourhost:port $0 [--verbose]"
    echo "   or: $0 https://yourhost:port [--verbose]"
    exit 1
fi

# ── Colours ───────────────────────────────────────────────────────────────────
GREEN='\033[0;32m'; RED='\033[0;31m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
pass() { echo -e "${GREEN}  PASS${NC}  $*"; }
fail() { echo -e "${RED}  FAIL${NC}  $*"; FAILURES=$((FAILURES+1)); }
info() { echo -e "${CYAN}  ----${NC}  $*"; }
warn() { echo -e "${YELLOW}  WARN${NC}  $*"; }

PASSES=0
FAILURES=0

# ── Helpers ───────────────────────────────────────────────────────────────────
get() {
    curl -sf --max-time 40 "$BASE$1"
}

post() {
    curl -sf --max-time 40 -X POST -H "Content-Type: application/json" -d "$2" "$BASE$1"
}

have_ffprobe() {
    command -v ffprobe >/dev/null 2>&1
}

ffprobe_stream() {
    local url="$1"
    local headers_json="${2:-{}}"

    python3 - "$url" "$headers_json" <<'PY'
import json, subprocess, sys
url = sys.argv[1]
headers = {}
try:
    headers = json.loads(sys.argv[2] or "{}")
except Exception:
    headers = {}

cmd = [
    "ffprobe",
    "-v", "error",
    "-print_format", "json",
    "-show_streams",
    "-show_format",
    "-rw_timeout", "15000000",
]

if headers:
    joined = "".join(f"{k}: {v}\r\n" for k, v in headers.items())
    cmd.extend(["-headers", joined])

cmd.append(url)

proc = subprocess.run(cmd, capture_output=True, text=True)
if proc.returncode != 0:
    print(json.dumps({"ok": False, "error": proc.stderr.strip() or proc.stdout.strip()}))
    sys.exit(0)

try:
    payload = json.loads(proc.stdout or "{}")
except Exception as ex:
    print(json.dumps({"ok": False, "error": f"invalid ffprobe output: {ex}"}))
    sys.exit(0)

streams = payload.get("streams") or []
has_video = any(s.get("codec_type") == "video" for s in streams)
has_audio = any(s.get("codec_type") == "audio" for s in streams)
print(json.dumps({
    "ok": True,
    "streams": len(streams),
    "has_video": has_video,
    "has_audio": has_audio
}))
PY
}

# Run a python3 check against JSON. Usage: check <label> <json> <python_expr_returning_bool> [warn_only]
check() {
    local label="$1" json="$2" expr="$3" warn_only="${4:-false}"
    local result
    result=$(echo "$json" | python3 -c "
import json,sys
try:
    d=json.load(sys.stdin)
    result = bool($expr)
    print('1' if result else '0')
except Exception as e:
    print('0')
    sys.stderr.write(str(e)+'\n')
" 2>/dev/null)

    if [[ "$result" == "1" ]]; then
        pass "$label"
        PASSES=$((PASSES+1))
    elif [[ "$warn_only" == "true" ]]; then
        warn "$label"
    else
        fail "$label"
        if [[ "$VERBOSE" == "true" ]]; then
            echo "$json" | python3 -m json.tool --no-ensure-ascii 2>/dev/null | head -40 || true
        fi
    fi
}

# ── Test suite ────────────────────────────────────────────────────────────────
echo ""
echo "TvClient v1 — E2E smoke tests"
echo "BASE: $BASE"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

echo ""
echo "[ 0 ] GET /api/tv/v1/version"
RESP=$(get "/api/tv/v1/version") || { fail "HTTP request failed"; RESP="{}"; }
check "version: has module name"       "$RESP" "d['data'].get('module') == 'TvClient'"
check "version: has asm_version"       "$RESP" "bool(d['data'].get('asm_version'))"
check "version: has server_utc"        "$RESP" "bool(d['data'].get('server_utc'))"
check "version: has contract version"  "$RESP" "bool(d['data'].get('api_contract_version'))"

echo ""
echo "[ 0.1 ] GET /api/tv/v1/debug/upstream"
RESP_DBG=$(get "/api/tv/v1/debug/upstream?media=tv&tmdbId=76479&provider=phantom&source=cub&lang=en-US") || { fail "HTTP request failed"; RESP_DBG="{}"; }
check "debug: has events_query"        "$RESP_DBG" "bool(d['data'].get('events_query'))"
check "debug: source=cub propagated"   "$RESP_DBG" "d['data'].get('source') == 'cub'"
check "debug: auth flags object"       "$RESP_DBG" "isinstance(d['data'].get('auth_flags'), dict)"

# Wait for server to be ready (Roslyn compilation can take 10-20s after restart)
echo -n "Waiting for server..."
for i in $(seq 1 30); do
    if curl -sf --max-time 3 "$BASE/api/tv/v1/home" >/dev/null 2>&1; then
        echo " ready."
        break
    fi
    echo -n "."
    sleep 2
    if [[ $i -eq 30 ]]; then
        echo " timed out!"
        echo "Server did not become ready within 60s. Check the container." >&2
        exit 1
    fi
done

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "[ 1 ] GET /api/tv/v1/home"
RESP=$(get "/api/tv/v1/home?lang=en-US&page=1") || { fail "HTTP request failed"; RESP="{}"; }
check "returns data object"            "$RESP" "isinstance(d.get('data'), dict)"
check "has trending_movies list"       "$RESP" "isinstance(d['data'].get('trending_movies'), list)"
check "trending_movies non-empty"      "$RESP" "len(d['data']['trending_movies']) > 0"
check "has trending_tv list"           "$RESP" "isinstance(d['data'].get('trending_tv'), list)"
check "trending_tv non-empty"          "$RESP" "len(d['data']['trending_tv']) > 0"
check "watching_now disabled"          "$RESP" "d['data']['watching_now']['enabled'] == False"
check "movie card has tmdb_id"         "$RESP" "d['data']['trending_movies'][0].get('tmdb_id', 0) > 0"
check "movie card has poster"          "$RESP" "bool(d['data']['trending_movies'][0].get('poster'))"
check "no error field"                 "$RESP" "d.get('error') is None"

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "[ 2 ] GET /api/tv/v1/movies"
for FEED in trending now_playing popular; do
    RESP=$(get "/api/tv/v1/movies?feed=$FEED&page=1&lang=en-US") || { fail "HTTP $FEED"; continue; }
    check "feed=$FEED: results non-empty"   "$RESP" "len(d['data']['results']) > 0"
    check "feed=$FEED: media=movie"         "$RESP" "d['data']['media'] == 'movie'"
    check "feed=$FEED: total_pages > 0"     "$RESP" "d['data']['total_pages'] > 0"
done

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "[ 3 ] GET /api/tv/v1/tv"
for FEED in trending popular on_the_air airing_today; do
    RESP=$(get "/api/tv/v1/tv?feed=$FEED&page=1&lang=en-US") || { fail "HTTP $FEED"; continue; }
    check "feed=$FEED: results non-empty"   "$RESP" "len(d['data']['results']) > 0"
    check "feed=$FEED: media=tv"            "$RESP" "d['data']['media'] == 'tv'"
done

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "[ 4 ] GET /api/tv/v1/search"
RESP=$(get "/api/tv/v1/search?q=breaking+bad&media=tv&lang=en-US") || { fail "HTTP request failed"; RESP="{}"; }
check "results non-empty"              "$RESP" "len(d['data']['results']) > 0"
check "Breaking Bad in results"        "$RESP" "any('breaking bad' in r.get('title','').lower() for r in d['data']['results'])"
check "all results are tv"             "$RESP" "all(r['media']=='tv' for r in d['data']['results'])"

RESP=$(get "/api/tv/v1/search?q=fight+club&media=movie&lang=en-US") || { fail "HTTP request failed"; RESP="{}"; }
check "movie search: results non-empty" "$RESP" "len(d['data']['results']) > 0"
check "movie search: all media=movie"   "$RESP" "all(r['media']=='movie' for r in d['data']['results'])"

RESP=$(get "/api/tv/v1/search") || true
check "missing q returns 400"           "${RESP:-{\"}error\\\":{}}}" "d.get('error') is not None" true

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "[ 5 ] GET /api/tv/v1/title/{media}/{tmdbId}"
# Breaking Bad (TV, tmdb=1396)
RESP=$(get "/api/tv/v1/title/tv/1396?lang=en-US") || { fail "HTTP request failed"; RESP="{}"; }
check "BB: title present"              "$RESP" "bool(d['data'].get('title'))"
check "BB: year=2008"                  "$RESP" "d['data']['year'] == 2008"
check "BB: has providers"              "$RESP" "len(d['data']['providers']['items']) > 0"
check "BB: default_provider set"       "$RESP" "bool(d['data']['providers']['default_provider'])"
check "BB: recommendations present"    "$RESP" "len(d['data'].get('recommendations',[])) > 0"
check "BB: provider has priority field" "$RESP" "'priority' in d['data']['providers']['items'][0]"

# Fight Club (movie, tmdb=550)
RESP=$(get "/api/tv/v1/title/movie/550?lang=en-US") || { fail "HTTP request failed"; RESP="{}"; }
check "FC: title present"              "$RESP" "bool(d['data'].get('title'))"
check "FC: year=1999"                  "$RESP" "d['data']['year'] == 1999"
check "FC: media=movie"                "$RESP" "d['data']['media'] == 'movie'"

# Non-existent
RESP=$(get "/api/tv/v1/title/movie/9999999999") || true
check "invalid tmdbId returns error"   "${RESP:-{\"error\":{}}}" "d.get('error') is not None" true

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "[ 6 ] GET /api/tv/v1/title/{media}/{tmdbId}/providers"
RESP=$(get "/api/tv/v1/title/tv/1396/providers?source=cub") || { fail "HTTP request failed"; RESP="{}"; }
check "providers list non-empty"       "$RESP" "len(d['data']['items']) > 0"
check "default_provider set"           "$RESP" "bool(d['data']['default_provider'])"
check "each item has code+url"         "$RESP" "all(i.get('code') and i.get('url') for i in d['data']['items'])"
check "each item has priority"         "$RESP" "all('priority' in i for i in d['data']['items'])"
check "each item has supports_series"  "$RESP" "all('supports_series' in i for i in d['data']['items'])"
check "each item has supports_movie"   "$RESP" "all('supports_movie' in i for i in d['data']['items'])"

# Collect default provider for use in later tests
DEFAULT_PROVIDER=$(echo "$RESP" | python3 -c "import json,sys; print(json.load(sys.stdin)['data']['default_provider'])" 2>/dev/null || echo "phantom")
info "default provider: $DEFAULT_PROVIDER"

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "[ 7 ] GET /api/tv/v1/title/{media}/{tmdbId}/providers/{provider}/options"
TV_OPTIONS_ID="${TV_OPTIONS_ID:-76479}"
TV_OPTIONS_SEASON="${TV_OPTIONS_SEASON:-1}"
RESP=$(get "/api/tv/v1/title/tv/${TV_OPTIONS_ID}/providers/$DEFAULT_PROVIDER/options?season=${TV_OPTIONS_SEASON}&source=cub&lang=en-US") || { fail "HTTP request failed"; RESP="{}"; }
check "options: no error"              "$RESP" "d.get('error') is None"
check "options: selected_season matches request" "$RESP" "d['data']['selected_season'] == ${TV_OPTIONS_SEASON}"
check "options: seasons list"          "$RESP" "len(d['data']['seasons']) > 0"
check "options: all 5 seasons for The Boys" "$RESP" "len(d['data']['seasons']) == 5"
check "options: has provider_status"   "$RESP" "bool(d['data'].get('provider_status'))"
check "options: has parse_source"      "$RESP" "bool(d['data'].get('parse_source'))"
check "options: has proxy_mode"        "$RESP" "bool(d['data'].get('proxy_mode'))"

AVAIL=$(echo "$RESP" | python3 -c "import json,sys; eps=json.load(sys.stdin)['data']['episodes']; print(len([e for e in eps if e['status']=='available']))" 2>/dev/null || echo "0")
EPS_COUNT=$(echo "$RESP" | python3 -c "import json,sys; print(len(json.load(sys.stdin)['data'].get('episodes',[])))" 2>/dev/null || echo "0")
info "available episodes in season ${TV_OPTIONS_SEASON}: $AVAIL"
if [[ "$EPS_COUNT" -gt 0 ]]; then
    check "options: episode has status"    "$RESP" "bool(d['data']['episodes'][0].get('status'))"
    check "options: episode has air_date field" "$RESP" "'air_date' in d['data']['episodes'][0]"
else
    warn "No episodes in options payload for provider=$DEFAULT_PROVIDER tv=${TV_OPTIONS_ID} season=${TV_OPTIONS_SEASON}"
fi
if [[ "$AVAIL" -gt 0 ]]; then
    check "available ep has play object"   "$RESP" "next((e for e in d['data']['episodes'] if e['status']=='available'), {}).get('play') is not None"
    check "available ep has qualities"     "$RESP" "len(next((e for e in d['data']['episodes'] if e['status']=='available'), {}).get('available_qualities',[])) > 0"
else
    warn "No available episodes from $DEFAULT_PROVIDER for tv=${TV_OPTIONS_ID} season=${TV_OPTIONS_SEASON} — provider may need auth/token"
fi

# Movie options (Fight Club)
RESP_M=$(get "/api/tv/v1/title/movie/550/providers/$DEFAULT_PROVIDER/options?lang=en-US") || { fail "HTTP request failed"; RESP_M="{}"; }
check "movie options: no error"        "$RESP_M" "d.get('error') is None"
check "movie options: movie_streams"   "$RESP_M" "isinstance(d['data'].get('movie_streams'), list)"
check "movie options: no episodes"     "$RESP_M" "len(d['data'].get('episodes',[])) == 0"

# Unknown provider
RESP_UNK=$(get "/api/tv/v1/title/tv/1396/providers/doesnotexist/options") || true
check "unknown provider returns error" "${RESP_UNK:-{\"error\":{}}}" "d.get('error') is not None" true

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "[ 8 ] POST /api/tv/v1/play/resolve"
# Try resolve if we have available episodes
if [[ "$AVAIL" -gt 0 ]]; then
    TRANSLATIONS=$(echo "$RESP" | python3 -c "
import json,sys
d=json.load(sys.stdin)
trs=d['data'].get('translations',[])
print(trs[0]['id'] if trs else '')
" 2>/dev/null || echo "")

    PAYLOAD=$(python3 -c "import json; print(json.dumps({'media':'tv','tmdbId':1396,'provider':'$DEFAULT_PROVIDER','season':1,'episode':1,'translation':'$TRANSLATIONS','lang':'en-US'}))")
    RESP_R=$(post "/api/tv/v1/play/resolve" "$PAYLOAD") || { fail "HTTP request failed"; RESP_R="{}"; }
    check "resolve: no error"              "$RESP_R" "d.get('error') is None"
    check "resolve: has play.url"          "$RESP_R" "bool(d['data']['play']['url'])"
    check "resolve: has stream_type"       "$RESP_R" "bool(d['data']['play']['stream_type'])"
    check "resolve: stream_type known"     "$RESP_R" "d['data']['play']['stream_type'] in ('hls','dash','file','unknown')"
    check "resolve: url is proxied"        "$RESP_R" "'/proxy/' in d['data']['play']['url']"
    check "resolve: has selected"          "$RESP_R" "isinstance(d['data']['selected'], dict)"
    check "resolve: has up_next list"      "$RESP_R" "isinstance(d['data']['up_next'], list)"
    info "play url: $(echo "$RESP_R" | python3 -c "import json,sys; print(json.load(sys.stdin)['data']['play']['url'][:80])" 2>/dev/null)"
else
    warn "Skipping resolve test — no available episodes from $DEFAULT_PROVIDER"
fi

# Bad request (missing provider)
RESP_BAD=$(post "/api/tv/v1/play/resolve" '{"media":"tv","tmdbId":1396}') || true
check "resolve: missing provider → error" "${RESP_BAD:-{\"error\":{}}}" "d.get('error') is not None" true

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "[ 9 ] TV provider matrix + resolve/ffprobe diagnostics (The Boys tmdb=76479)"
TV_ID="${TV_ID:-76479}"
TV_SEASON="${TV_SEASON:-5}"
TV_EPISODE="${TV_EPISODE:-4}"
STRICT_TV="${STRICT_TV:-false}"

RESP_TV_PROV=$(get "/api/tv/v1/title/tv/${TV_ID}/providers?source=cub&lang=en-US") || { fail "tv providers list failed"; RESP_TV_PROV="{}"; }
check "tv providers: has list" "$RESP_TV_PROV" "len(d['data']['items']) > 0"

TV_PROVIDERS_RAW=$(echo "$RESP_TV_PROV" | python3 -c "import json,sys; d=json.load(sys.stdin); print('\n'.join(i['code'] for i in d['data'].get('items',[])))" 2>/dev/null || true)
PLAYABLE_PROVIDER=""

while IFS= read -r P; do
    [[ -z "$P" ]] && continue
    OPT=$(get "/api/tv/v1/title/tv/${TV_ID}/providers/${P}/options?source=cub&lang=en-US&season=${TV_SEASON}") || { warn "provider=${P}: options request failed"; continue; }

    trans=$(echo "$OPT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(len(d.get('data',{}).get('translations',[])))" 2>/dev/null || echo 0)
    eps=$(echo "$OPT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(len(d.get('data',{}).get('episodes',[])))" 2>/dev/null || echo 0)
    playable=$(echo "$OPT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(len([e for e in d.get('data',{}).get('episodes',[]) if e.get('status')=='available' and e.get('play')]))" 2>/dev/null || echo 0)
    statuses=$(echo "$OPT" | python3 -c "import json,sys; d=json.load(sys.stdin); s=sorted(set([e.get('status','') for e in d.get('data',{}).get('episodes',[])])); print(','.join(s))" 2>/dev/null || echo "")
    info "provider=${P} season=${TV_SEASON} translations=${trans} episodes=${eps} playable=${playable} statuses=${statuses}"

    if [[ -z "$PLAYABLE_PROVIDER" && "$playable" -gt 0 ]]; then
        PLAYABLE_PROVIDER="$P"
    fi
done <<< "$TV_PROVIDERS_RAW"

if [[ -z "$PLAYABLE_PROVIDER" ]]; then
    if [[ "$STRICT_TV" == "true" ]]; then
        fail "no providers with playable episodes for tv=${TV_ID} season=${TV_SEASON}"
    else
        warn "no providers with playable episodes for tv=${TV_ID} season=${TV_SEASON} (diagnostic only)"
    fi
else
    info "selected playable provider: $PLAYABLE_PROVIDER"
    payload=$(python3 -c "import json; print(json.dumps({'media':'tv','tmdbId':${TV_ID},'source':'cub','provider':'${PLAYABLE_PROVIDER}','season':${TV_SEASON},'episode':${TV_EPISODE},'lang':'en-US'}))")
    RESOLVE=$(post "/api/tv/v1/play/resolve" "$payload") || { fail "resolve for playable provider failed"; RESOLVE="{}"; }
    check "tv resolve: no error" "$RESOLVE" "d.get('error') is None"
    check "tv resolve: has play.url" "$RESOLVE" "bool(d['data']['play']['url'])"
    check "tv resolve: url is proxied" "$RESOLVE" "'/proxy/' in d['data']['play']['url']"

    if have_ffprobe; then
        URL=$(echo "$RESOLVE" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d.get('data',{}).get('play',{}).get('url',''))" 2>/dev/null || echo "")
        HDR=$(echo "$RESOLVE" | python3 -c "import json,sys; d=json.load(sys.stdin); import json as j; print(j.dumps(d.get('data',{}).get('play',{}).get('headers',{}) or {}))" 2>/dev/null || echo "{}")
        if [[ -n "$URL" ]]; then
            FP=$(ffprobe_stream "$URL" "$HDR")
            check "ffprobe: command succeeded" "$FP" "d.get('ok') == True"
            check "ffprobe: has streams" "$FP" "(d.get('streams') or 0) > 0"
        else
            warn "ffprobe skipped: empty play.url"
        fi
    else
        warn "ffprobe is not installed; skipping stream probe checks"
    fi
fi

# Explicitly verify expected resolve_failed for known-unplayable combination.
UNPLAYABLE_PAYLOAD=$(python3 -c "import json; print(json.dumps({'media':'tv','tmdbId':${TV_ID},'source':'cub','provider':'phantom','season':${TV_SEASON},'episode':${TV_EPISODE},'lang':'en-US'}))")
UNPLAYABLE_RESOLVE=$(post "/api/tv/v1/play/resolve" "$UNPLAYABLE_PAYLOAD") || true
check "tv unplayable resolve returns error envelope" "${UNPLAYABLE_RESOLVE:-{\"error\":{}}}" "d.get('error') is not None" true

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
TOTAL=$((PASSES+FAILURES))
echo -e "Results: ${GREEN}${PASSES} passed${NC} / ${RED}${FAILURES} failed${NC} / ${TOTAL} total"
echo ""
[[ "$FAILURES" -eq 0 ]] && echo -e "${GREEN}All tests passed.${NC}" && exit 0
echo -e "${RED}${FAILURES} test(s) failed.${NC}" && exit 1
