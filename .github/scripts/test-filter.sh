#!/bin/sh
# Prints the dotnet test --filter expression for one part of the release tests.
# Usage: sh .github/scripts/test-filter.sh .github/test-part-1.txt "$TEST_PART"
# The list holds one name per line (a test class, or a prefix of fully qualified test names); blank lines and
# lines starting with # are ignored. Part 1 is every test whose fully qualified name contains a listed name:
#   FullyQualifiedName~A|FullyQualifiedName~B|...
# Part 2 is the exact negation, every other test, so a test class that is not listed still runs (in part 2):
#   FullyQualifiedName!~A&FullyQualifiedName!~B&...
# A name must match ^[A-Za-z_][A-Za-z0-9_.]*$, so no filter operator (| & ! ~ = ( ) \ ,), space or shell text
# can get into the expression. On any error (a part other than 1 or 2, a missing list, a list with no names, a
# bad name) it prints a message on stderr, nothing on stdout, and exits 1.
# LC_ALL=C makes the character classes plain ASCII in every runner locale.
set -eu

LC_ALL=C
export LC_ALL

list="${1-}"
part="${2-}"

case "$part" in
  1) term='FullyQualifiedName~'; join='|' ;;
  2) term='FullyQualifiedName!~'; join='&' ;;
  *)
    echo "part '$part' is not 1 or 2" >&2
    exit 1
    ;;
esac

if [ -z "$list" ] || [ ! -f "$list" ]; then
  echo "test list '$list' is not a file" >&2
  exit 1
fi

filter=''
number=0
while IFS= read -r line || [ -n "$line" ]; do
  number=$((number + 1))
  case "$line" in
    '#'*) continue ;;
  esac
  case "$line" in
    *[!' 	']*) ;;
    *) continue ;;
  esac
  case "$line" in
    [!A-Za-z_]* | *[!A-Za-z0-9_.]*)
      echo "line $number of $list is not a test name (only letters, digits, _ and ., not starting with a digit or .)" >&2
      exit 1
      ;;
  esac
  if [ -z "$filter" ]; then
    filter="$term$line"
  else
    filter="$filter$join$term$line"
  fi
done < "$list"

if [ -z "$filter" ]; then
  echo "test list $list holds no names" >&2
  exit 1
fi

printf '%s\n' "$filter"
