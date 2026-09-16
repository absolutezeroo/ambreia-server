// SPDX-License-Identifier: MIT
pragma solidity 0.8.28;

import {Script} from "forge-std/Script.sol";
import {console} from "forge-std/console.sol";
import {VortexCoin} from "../src/VortexCoin.sol";
import {VortexCollectible} from "../src/VortexCollectible.sol";

/// @notice Puts both contracts on the chain and prints their addresses.
///
/// @dev Run against a local anvil:
///
///      forge script script/Deploy.s.sol --rpc-url http://127.0.0.1:8545 --broadcast \
///        --private-key <treasury key>
///
///      ON A FRESH CHAIN THE TWO ADDRESSES ARE FIXED, and that is the point of deploying them
///      first and together. A contract's address is derived from the deployer and its nonce, so a
///      treasury that has sent nothing else always lands VortexCoin at nonce 0 and VortexCollectible
///      at nonce 1 — the same two addresses every time the chain is recreated. They can therefore be
///      written into configuration as constants, and nothing has to discover them at startup or
///      keep a registry of where the last deployment went.
///
///      That guarantee is exactly as strong as "the treasury has sent nothing else first". Deploy
///      before anything else touches that account, or the addresses move.
contract Deploy is Script {
    /// @dev Where a Relic's metadata is served from. Overridable because a real hotel is not on
    ///      localhost, and the value is baked into the contract at construction.
    function baseUri() internal view returns (string memory) {
        try vm.envString("VORTEX_COLLECTIBLE_BASE_URI") returns (string memory configured) {
            return configured;
        } catch {
            return "http://localhost:8080/api/public/collectibles/";
        }
    }

    function run() external {
        // The broadcasting account is the treasury: it owns both contracts, it is the only account
        // that can mint, and it is the one the emulator derives its own key for.
        vm.startBroadcast();

        address treasury = msg.sender;

        VortexCoin coin = new VortexCoin(treasury);
        VortexCollectible relics = new VortexCollectible(treasury, baseUri());

        vm.stopBroadcast();

        console.log("treasury          ", treasury);
        console.log("VortexCoin        ", address(coin));
        console.log("VortexCollectible ", address(relics));
    }
}
