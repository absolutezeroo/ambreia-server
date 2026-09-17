#!/bin/sh
# =================================================================================================
# Starts the hotel's chain and makes sure its two contracts are on it.
#
# Three things happen, in this order, every boot:
#   1. anvil starts, loading whatever state the volume holds.
#   2. if the chain is virgin, the contracts are deployed (and the treasury funded, see deploy.sh).
#   3. the two contract addresses are printed, whether they were just deployed or already there.
#
# Step 3 matters as much as step 2. The addresses are what an operator has to paste into the
# emulator's configuration, and asking them to scroll back to a deployment that happened weeks ago
# is how a configuration value gets guessed instead of read.
# =================================================================================================

set -eu

STATE_DIR="${VORTEX_CHAIN_STATE_DIR:-/data}"
STATE_FILE="$STATE_DIR/anvil-state.json"
# Not anvil's own 31337: a wallet checks the id against the public chain registry before it will add
# a network, and 31337 is registered there as GoChain Testnet, whose coin is GO. MetaMask then
# refuses ours with "the native token symbol does not match". 867839 is unregistered, and spells
# VORTEX on a phone keypad. It must stay in step with Vortex:Chain:ChainId on the emulator.
CHAIN_ID="${VORTEX_CHAIN_ID:-867839}"
RPC_URL="${VORTEX_CHAIN_RPC_URL:-http://127.0.0.1:8545}"

# Overridable so this script can be exercised outside a container, against a real anvil, which is
# the only way it gets tested on a machine that has no Docker.
DEPLOY_SCRIPT="${VORTEX_CHAIN_DEPLOY_SCRIPT:-/usr/local/bin/deploy.sh}"

if ! mkdir -p "$STATE_DIR" 2>/dev/null || ! [ -w "$STATE_DIR" ]; then
    # Refused rather than started. A node that cannot persist looks identical to one that can until
    # the first restart, at which point the chain is empty and every balance on it is gone.
    echo "FATAL: $STATE_DIR is not writable, so the chain could not keep its state." >&2
    echo "Mount a volume there that this container can write to." >&2
    exit 1
fi

echo "vortex-chain: starting anvil on chain $CHAIN_ID, state in $STATE_FILE"

# --state loads on start and saves on exit; --state-interval also saves while running, so an
# ungraceful stop loses at most that many seconds rather than everything since boot.
anvil \
    --host 0.0.0.0 \
    --chain-id "$CHAIN_ID" \
    --state "$STATE_FILE" \
    --state-interval 10 \
    --silent &

ANVIL_PID=$!

# A container stop is a SIGTERM, and anvil flushes its state on one. Without this trap the signal
# reaches the shell and not anvil, the shell exits, and the node is killed with whatever it had not
# written yet.
stop() {
    echo "vortex-chain: stopping, flushing state"
    kill -TERM "$ANVIL_PID" 2>/dev/null || true
    wait "$ANVIL_PID" 2>/dev/null || true
    exit 0
}

trap stop TERM INT

attempt=0

until cast chain-id --rpc-url "$RPC_URL" >/dev/null 2>&1; do
    attempt=$((attempt + 1))

    if [ "$attempt" -ge 60 ]; then
        echo "FATAL: anvil did not answer on $RPC_URL within 60s." >&2
        exit 1
    fi

    sleep 1
done

if [ -z "${VORTEX_CHAIN_TREASURY_KEY:-}" ]; then
    echo "vortex-chain: no VORTEX_CHAIN_TREASURY_KEY, so no contracts were deployed."
    echo "vortex-chain: the node is running and is otherwise useless. Generate a key with"
    echo "vortex-chain:   openssl rand -hex 32"
    echo "vortex-chain: set it here AND on the emulator, and restart this service."
else
    TREASURY=$(cast wallet address --private-key "$VORTEX_CHAIN_TREASURY_KEY")
    COIN=$(cast compute-address "$TREASURY" --nonce 0 | sed 's/.*: //')
    COLLECTIBLE=$(cast compute-address "$TREASURY" --nonce 1 | sed 's/.*: //')

    if [ "$(cast code "$COIN" --rpc-url "$RPC_URL")" = "0x" ]; then
        # Said loudly, because this line is the only moment at which the loss is visible.
        #
        # A virgin chain is correct exactly once: the first boot ever. Every other time it means
        # $STATE_DIR did not keep what was written to it, and the contracts are about to be
        # redeployed onto an empty ledger -- every VTX balance and every Relic gone, with no other
        # copy anywhere. This container cannot tell the two cases apart, because any marker that
        # would say so would live on the volume that just vanished. So it states what it found and
        # what that implies, and leaves the judgement to whoever reads it.
        #
        # It has happened: 2026-09-17, a Coolify resource with no persistent storage on /data. The
        # compose file mounts a NAMED volume, but a bare `VOLUME ["/data"]` in the Dockerfile gets an
        # ANONYMOUS one, which survives a container restart and not a redeployment. This line used to
        # read "chain is virgin, deploying" and nothing else, so the chain was recreated in silence
        # and the first anyone noticed was an empty leaderboard two hours later.
        echo "vortex-chain: ==============================================================" >&2
        echo "vortex-chain: NO CONTRACTS ON THIS CHAIN, AND NO STATE IN $STATE_DIR." >&2
        echo "vortex-chain: Deploying fresh ones. This is correct ONLY on a first boot." >&2
        echo "vortex-chain: If this hotel had balances, THEY ARE GONE and $STATE_DIR is" >&2
        echo "vortex-chain: not persisting. Mount a NAMED volume there -- an anonymous one" >&2
        echo "vortex-chain: is replaced on every redeployment." >&2
        echo "vortex-chain: ==============================================================" >&2

        # Deliberately not fatal. The node holds state and a restart loop on a stateful service is
        # worse than a running node with a loud complaint: an operator can read this, fix the cause
        # and restart once, where a crash loop just churns.
        if ! VORTEX_CHAIN_RPC_URL="$RPC_URL" sh "$DEPLOY_SCRIPT"; then
            echo "vortex-chain: DEPLOYMENT FAILED. The node is up and has no contracts on it;" >&2
            echo "vortex-chain: the emulator will read every balance as zero until this is fixed." >&2
        fi
    else
        echo "vortex-chain: contracts already deployed"
    fi

    echo "vortex-chain: ---------------------------------------------------------------"
    echo "vortex-chain: treasury                          $TREASURY"
    echo "vortex-chain: VORTEX__Vortex__Chain__CoinAddress        $COIN"
    echo "vortex-chain: VORTEX__Vortex__Chain__CollectibleAddress $COLLECTIBLE"
    echo "vortex-chain: ---------------------------------------------------------------"
fi

wait "$ANVIL_PID"
