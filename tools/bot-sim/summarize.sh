#!/usr/bin/env bash
# Summarizes the bot AI simulation (UT_BotSim) for a GitHub Actions run: a table in the run summary and a warning
# annotation per finding of the live rules. Usage: summarize.sh <bot-sim folder>. See docs/BOT-AI-SIMULATION.md.
set -u
dir="${1:-bot-sim}"
tsv="$dir/findings.tsv"
if [ ! -f "$tsv" ]; then
  echo "::warning title=Bot AI simulation::no findings.tsv in $dir (did UT_BotSim run?)"
  exit 0
fi
pass=$(grep -c '^pass' "$tsv" || true)
found=$(grep -c '^FINDING' "$tsv" || true)
echo "== findings.tsv =="
cat "$tsv"
for md in "$dir"/*.md; do
  [ -f "$md" ] || continue
  echo "== $(basename "$md") (tables; timeline in the artifact) =="
  sed -n '1,/^## Timeline/p' "$md"
done
echo "::notice title=Bot AI simulation::$pass expectations met, $found findings (all variants). Reports: bot-sim artifact."
while IFS=$'\t' read -r result scenario variant expectation measured; do
  if [ "$result" = "FINDING" ] && [ "$variant" = "current" ]; then
    echo "::warning title=Bot sim finding ($scenario)::$expectation -> $measured"
  fi
done < "$tsv"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "## Bot AI simulation (tests only)"
    echo
    echo "| Result | Scenario | Variant | Expectation | Measured |"
    echo "|---|---|---|---|---|"
    while IFS=$'\t' read -r result scenario variant expectation measured; do
      echo "| $result | $scenario | $variant | $expectation | $measured |"
    done < "$tsv"
    echo
    echo "Full reports with timelines: the **bot-sim** artifact (one .md and .json per scenario)."
  } >> "$GITHUB_STEP_SUMMARY"
fi
exit 0
