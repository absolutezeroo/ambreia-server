#!/bin/sh
# =================================================================================================
# Puts VortexCoin and VortexCollectible on the chain, from the committed artifacts.
#
# Runs inside the official foundry image, which carries the foundry binaries and very little else —
# so this uses `cast` and `cat` and nothing more. No solc, no npm, no forge-std: the bytecode was
# compiled once by a pinned solc (see chain/foundry.toml) and exported by
# tools/chain/export-artifacts.mjs, and compiling it again in the container would only introduce a
# second compiler that might not agree.
#
#   docker compose run --rm chain-deploy
#
# ON A FRESH CHAIN THE ADDRESSES ARE FIXED, because a contract's address comes from the deployer and
# its nonce: a treasury that has sent nothing else puts VortexCoin at nonce 0 and VortexCollectible
# at nonce 1, every time. That is why they can be configuration constants. It holds exactly as long
# as this runs BEFORE anything else spends from the treasury.
#
# Deploying twice puts a SECOND, empty pair of contracts on the chain at different addresses, and
# nothing here stops that — so the guard below refuses when code already exists at the expected
# address, which is the whole failure mode worth catching.
# =================================================================================================

set -eu

RPC_URL="${VORTEX_CHAIN_RPC_URL:-http://chain:8545}"
ARTIFACTS="${VORTEX_CHAIN_ARTIFACTS:-/chain/artifacts}"
BASE_URI="${VORTEX_COLLECTIBLE_BASE_URI:-http://localhost:8080/api/public/collectibles/}"

if [ -z "${VORTEX_CHAIN_TREASURY_KEY:-}" ]; then
    echo "VORTEX_CHAIN_TREASURY_KEY is not set: nothing can be deployed without the account that" >&2
    echo "will own both contracts." >&2
    exit 1
fi

TREASURY=$(cast wallet address --private-key "$VORTEX_CHAIN_TREASURY_KEY")

echo "rpc      : $RPC_URL"
echo "treasury : $TREASURY"

# Wait for the node rather than assuming compose ordering did it. `depends_on` waits for the
# container, not for anvil's JSON-RPC to be answering, and the difference is a deploy that fails on
# a cold start and succeeds on every retry afterwards — the worst kind of intermittent.
attempt=0
until cast chain-id --rpc-url "$RPC_URL" >/dev/null 2>&1; do
    attempt=$((attempt + 1))

    if [ "$attempt" -ge 60 ]; then
        echo "The chain at $RPC_URL never answered." >&2
        exit 1
    fi

    sleep 1
done

# A treasury with no gas cannot deploy, and a real hotel's treasury key is one an operator generated
# — so it starts at zero on a chain whose coins are all in anvil's genesis accounts. Funding it is
# one transaction that has to happen from INSIDE this network, because the node's port is not
# published; asking an operator to run it themselves means handing them a command that does not work
# from the shell they would naturally type it in. So it happens here.
#
# The funding account defaults to anvil's first genesis address, whose key is public knowledge and
# which is exactly why it must not be the treasury. On a chain with no known genesis account, set
# VORTEX_CHAIN_FUNDING_KEY to something that has a balance, or fund the treasury beforehand and this
# does nothing.
FUNDING_KEY="${VORTEX_CHAIN_FUNDING_KEY:-0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80}"
FUNDING_AMOUNT="${VORTEX_CHAIN_FUNDING_AMOUNT:-1000ether}"

if [ "$(cast balance "$TREASURY" --rpc-url "$RPC_URL")" = "0" ]; then
    FUNDER=$(cast wallet address --private-key "$FUNDING_KEY")

    if [ "$FUNDER" = "$TREASURY" ]; then
        echo "The treasury has no balance and is its own funding account; nothing can pay for the" >&2
        echo "deployment. Send it some of the chain's native coin first." >&2
        exit 1
    fi

    echo "funding  : $FUNDING_AMOUNT from $FUNDER"

    if ! cast send "$TREASURY" --value "$FUNDING_AMOUNT" \
        --rpc-url "$RPC_URL" --private-key "$FUNDING_KEY" >/dev/null; then
        echo "Could not fund the treasury from $FUNDER. Set VORTEX_CHAIN_FUNDING_KEY to an" >&2
        echo "account that holds something, or fund $TREASURY yourself." >&2
        exit 1
    fi
fi

deploy() {
    name="$1"
    signature="$2"
    shift 2

    # Every flag goes BEFORE --create. `cast send --create <CODE> <SIG> <ARGS>...` takes everything
    # after the signature as constructor arguments, so a trailing --rpc-url is not a flag there, it
    # is an argument — and the error it produces ("unexpected argument '--rpc-url' found") reads like
    # the flag is unsupported rather than misplaced.
    address=$(
        cast send \
            --rpc-url "$RPC_URL" \
            --private-key "$VORTEX_CHAIN_TREASURY_KEY" \
            --json \
            --create "$(cat "$ARTIFACTS/$name.hex")" "$signature" "$@" |
            sed -n 's/.*"contractAddress":"\([^"]*\)".*/\1/p'
    )

    if [ -z "$address" ]; then
        echo "$name: deployment returned no contract address." >&2
        exit 1
    fi

    echo "$name : $address"
}

# Already there? A second deployment is not an error the chain reports — it just makes another pair
# nobody is configured to talk to.
#
# The address is COMPUTED from this treasury rather than written down, because a written-down one is
# only correct for the treasury it was written down for: a hotel with its own key would deploy to a
# different address, find nothing at the hardcoded one, and sail straight past the guard.
FIRST_EXPECTED=$(cast compute-address "$TREASURY" --nonce 0 | sed 's/.*: //')

if [ "$(cast code "$FIRST_EXPECTED" --rpc-url "$RPC_URL")" != "0x" ]; then
    echo "A contract already exists at $FIRST_EXPECTED — this chain has been deployed to." >&2
    echo "Deploying again would create a second, unreferenced pair. Nothing done." >&2
    exit 1
fi

deploy VortexCoin "constructor(address)" "$TREASURY"
deploy VortexCollectible "constructor(address,string)" "$TREASURY" "$BASE_URI"
