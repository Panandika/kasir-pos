#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

fail=0

check_zero() {
  local name="$1" pattern="$2" globpath="$3"
  local count
  count=$({ grep -rEc "$pattern" $globpath 2>/dev/null || true; } | awk -F: '{s+=$2} END {print s+0}')
  if [ "$count" -ne 0 ]; then
    echo "FAIL: $name — $count occurrences of $pattern in $globpath"
    fail=1
  else
    echo "PASS: $name"
  fi
}

check_zero "no_hardcoded_00dc00" '#00dc00' 'Kasir.Avalonia/Forms'
check_zero "no_hardcoded_consolas" 'FontFamily="Consolas' 'Kasir.Avalonia/Forms'
check_zero "no_dropshadow" 'DropShadowEffect' 'Kasir.Avalonia/Forms'
check_zero "no_blur" 'BlurEffect' 'Kasir.Avalonia/Forms'
check_zero "no_opacitymask" 'OpacityMask' 'Kasir.Avalonia/Forms'
check_zero "no_acrylic" 'Acrylic' 'Kasir.Avalonia/Forms'

if [ $fail -ne 0 ]; then
  echo "CI assertions failed."
  exit 1
fi
echo "All CI grep assertions passed."
