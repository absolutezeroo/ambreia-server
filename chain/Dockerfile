# syntax=docker/dockerfile:1

# =================================================================================================
# The hotel's chain, as ONE deployable resource.
#
# A node on its own is not enough to be useful: the two contracts have to be on it, at addresses the
# emulator is configured with. Doing that as a separate one-shot container works in a compose stack
# and does not survive a deployment model where every service is its own resource — which is how
# this hotel is actually deployed. So the node deploys them itself, on first boot, and prints the
# addresses on every boot.
#
# THE BUILD CONTEXT IS THIS DIRECTORY, not the repository root:
#   docker build -f chain/Dockerfile -t vortex-chain chain
#
# Everything the image needs lives under chain/ for exactly that reason. It used to reach for
# tools/chain/deploy.sh, which forced the context up to the repository root — and a deployment
# platform that sets the build context to the directory holding the Dockerfile, which is the normal
# thing to do and what Coolify does, then failed every COPY with "not found". A context a build
# cannot choose is one the image must not depend on.
#
# THE RPC PORT MUST NOT BE PUBLISHED. anvil serves anvil_setBalance and anvil_impersonateAccount
# beside the standard methods with no way to switch them off; anyone who can reach this port can
# mint themselves anything or act as the treasury. The emulator reaches it over the internal
# network, and a player's wallet reaches it only through the filtered proxy on the web API.
# =================================================================================================

FROM ghcr.io/foundry-rs/foundry:v1.8.3

# Root on purpose. The state volume's ownership is decided by whoever mounts it — Coolify, compose,
# a bare docker run — and a UID that cannot write /data does not fail loudly here: anvil starts,
# serves, and silently keeps no state, so the chain is fresh again after the first restart and every
# balance on it is gone. Fighting that is a worse trade than running a node with no published port
# as root.
USER root

# The compiled contracts and the script that sends them. Both are committed, so this image needs
# neither solc nor npm: the bytecode was produced once by the pinned solc in chain/foundry.toml.
COPY artifacts /chain/artifacts
COPY deploy.sh /usr/local/bin/deploy.sh
COPY docker-entrypoint.sh /usr/local/bin/vortex-chain-entrypoint.sh

RUN chmod +x /usr/local/bin/deploy.sh /usr/local/bin/vortex-chain-entrypoint.sh \
    && mkdir -p /data

ENV VORTEX_CHAIN_ARTIFACTS=/chain/artifacts \
    VORTEX_CHAIN_RPC_URL=http://127.0.0.1:8545 \
    VORTEX_CHAIN_STATE_DIR=/data \
    VORTEX_CHAIN_ID=867839

EXPOSE 8545
VOLUME ["/data"]

# Liveness only: does the node answer JSON-RPC. `cast chain-id` is the same probe the entrypoint
# already waits on before it does anything else, and `cast` ships in this image — nothing is
# installed for this.
#
# Deliberately NOT "are the contracts deployed". docker-entrypoint.sh keeps the node up when a
# deployment fails, on the grounds that a stateful service in a restart loop is worse than one
# running with a loud complaint. A healthcheck that failed on missing contracts would restore
# exactly the crash loop that decision avoids, and roll the deployment back on top of it.
#
# start-period covers anvil loading the state file plus the first-boot deployment behind it; the
# entrypoint itself allows 60s for anvil to answer.
HEALTHCHECK --interval=30s --timeout=5s --start-period=90s --retries=3 \
    CMD cast chain-id --rpc-url "$VORTEX_CHAIN_RPC_URL" >/dev/null 2>&1 || exit 1

ENTRYPOINT ["/bin/sh", "/usr/local/bin/vortex-chain-entrypoint.sh"]
