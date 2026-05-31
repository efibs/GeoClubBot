#!/usr/bin/env bash
#
# new-usecase.sh — scaffold a use case (+ optional validator and Discord command)
# in the right folders with the right (short-RootNamespace) namespaces.
#
# See Documentation/DeveloperGuide.md for the full "how to add a command" recipe.
#
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: scripts/new-usecase.sh <Feature> <Name> <command|query> [options]

Arguments:
  <Feature>          PascalCase feature slice, e.g. Strikes, GeoGuessrAccountLinking.
                     Maps to UseCases/<Feature>/ and Interactions/<Feature>/.
  <Name>             PascalCase operation name WITHOUT the Command/Query suffix,
                     e.g. ArchiveStrike. The suffix is added automatically.
  command | query    Kind of request. command -> ICommand<Result<Guid>>,
                     query   -> IQuery<Result<string>>. Edit the response type after.

Options:
  --validator        Also generate a FluentValidation validator stub.
  --no-module        Do NOT generate / touch a Discord command module.
  -h, --help         Show this help.

Examples:
  scripts/new-usecase.sh Strikes ArchiveStrike command --validator
  scripts/new-usecase.sh Club GetClubBadges query --no-module

The generated files compile as-is; fill in the request fields, the handler body,
and (for the module) the slash-command signature, then run `dotnet build`.
If the Discord module already exists, the script prints the method to paste rather
than editing the existing file.
EOF
}

# ---- locate repo root (script lives in <root>/scripts) -----------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# ---- parse args --------------------------------------------------------------
GEN_VALIDATOR=0
GEN_MODULE=1
POSITIONAL=()
for arg in "$@"; do
    case "$arg" in
        --validator) GEN_VALIDATOR=1 ;;
        --no-module) GEN_MODULE=0 ;;
        -h|--help) usage; exit 0 ;;
        --*) echo "Unknown option: $arg" >&2; usage; exit 1 ;;
        *) POSITIONAL+=("$arg") ;;
    esac
done

if [ "${#POSITIONAL[@]}" -ne 3 ]; then
    usage; exit 1
fi

FEATURE="${POSITIONAL[0]}"
NAME="${POSITIONAL[1]}"
KIND="${POSITIONAL[2]}"

case "$KIND" in
    command) IFACE="ICommand"; RESP="Result<Guid>"; SUFFIX="Command"; OK_RETURN="Result<Guid>.Success(Guid.NewGuid())" ;;
    query)   IFACE="IQuery";   RESP="Result<string>"; SUFFIX="Query"; OK_RETURN="Result<string>.Success(string.Empty)" ;;
    *) echo "Third argument must be 'command' or 'query', got '$KIND'." >&2; exit 1 ;;
esac

REQUEST="${NAME}${SUFFIX}"      # e.g. ArchiveStrikeCommand
HANDLER="${NAME}Handler"        # e.g. ArchiveStrikeHandler

# PascalCase -> kebab-case (for Discord slash command + group names)
to_kebab() { sed -E 's/([A-Z])/-\L\1/g' <<<"$1" | sed 's/^-//'; }
NAME_KEBAB="$(to_kebab "$NAME")"
FEATURE_KEBAB="$(to_kebab "$FEATURE")"

# Truncate a Discord command/group name to the 32-char limit.
trunc32() { cut -c1-32 <<<"$1"; }
NAME_KEBAB="$(trunc32 "$NAME_KEBAB")"
FEATURE_KEBAB="$(trunc32 "$FEATURE_KEBAB")"

APP_DIR="$ROOT/GeoClubBot.Application/UseCases/$FEATURE"
VALIDATOR_DIR="$APP_DIR/Validators"
MODULE_DIR="$ROOT/GeoClubBot.Discord/InputAdapters/Interactions/$FEATURE"
MODULE_FILE="$MODULE_DIR/${FEATURE}Module.cs"

# ---- 1. use case (request record + handler) ----------------------------------
mkdir -p "$APP_DIR"
USECASE_FILE="$APP_DIR/${REQUEST}.cs"
if [ -e "$USECASE_FILE" ]; then
    echo "SKIP  $USECASE_FILE already exists" >&2
else
    cat > "$USECASE_FILE" <<EOF
using MediatR;
using UseCases.Abstractions;
using Utilities;

namespace UseCases.UseCases.$FEATURE;

public sealed record $REQUEST(/* TODO: add request parameters */) : $IFACE<$RESP>;

public sealed class $HANDLER : IRequestHandler<$REQUEST, $RESP>
{
    public Task<$RESP> Handle($REQUEST request, CancellationToken cancellationToken)
    {
        // TODO: implement the use case. Return a failure via an Error (see
        // Documentation/ResultConventions.md) for expected failure modes.
        return Task.FromResult($OK_RETURN);
    }
}
EOF
    echo "WROTE $USECASE_FILE"
fi

# ---- 2. validator (optional) -------------------------------------------------
if [ "$GEN_VALIDATOR" -eq 1 ]; then
    mkdir -p "$VALIDATOR_DIR"
    VALIDATOR_FILE="$VALIDATOR_DIR/${REQUEST}Validator.cs"
    if [ -e "$VALIDATOR_FILE" ]; then
        echo "SKIP  $VALIDATOR_FILE already exists" >&2
    else
        cat > "$VALIDATOR_FILE" <<EOF
using FluentValidation;

namespace UseCases.UseCases.$FEATURE.Validators;

public sealed class ${REQUEST}Validator : AbstractValidator<$REQUEST>
{
    public ${REQUEST}Validator()
    {
        // TODO: add rules, e.g. RuleFor(x => x.Foo).NotEmpty();
    }
}
EOF
        echo "WROTE $VALIDATOR_FILE"
    fi
fi

# ---- 3. Discord command module (optional) ------------------------------------
if [ "$GEN_MODULE" -eq 1 ]; then
    METHOD=$(cat <<EOF
    [SlashCommand("$NAME_KEBAB", "TODO: describe the $NAME command")]
    public Task ${NAME}Async() =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new $REQUEST(), ct)
                    .ConfigureAwait(false);

                // TODO: surface the result to the user.
                await FollowupAsync(
                        result.IsSuccess ? "Done." : FriendlyMessageFor(result.Error),
                        ephemeral: true)
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "$NAME failed. Please try again later. If the problem persists, contact an admin.");
EOF
)

    if [ -e "$MODULE_FILE" ]; then
        echo
        echo "NOTE  $MODULE_FILE already exists — add this method to it manually:"
        echo "----------------------------------------------------------------------"
        echo "$METHOD"
        echo "----------------------------------------------------------------------"
        echo "(and ensure 'using UseCases.UseCases.$FEATURE;' is present)"
    else
        mkdir -p "$MODULE_DIR"
        cat > "$MODULE_FILE" <<EOF
using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.$FEATURE;

namespace GeoClubBot.Discord.InputAdapters.Interactions.$FEATURE;

[CommandContextType(InteractionContextType.Guild)]
[Group("$FEATURE_KEBAB", "TODO: describe the $FEATURE commands")]
public class ${FEATURE}Module(
    ISender mediator,
    ILogger<${FEATURE}Module> logger) : ClubBotInteractionModule(mediator, logger)
{
$METHOD
}
EOF
        echo "WROTE $MODULE_FILE"
    fi
fi

echo
echo "Done. Next: fill in the TODOs, then run 'dotnet build GeoClubBot.sln'."
