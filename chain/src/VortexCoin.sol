// SPDX-License-Identifier: MIT
pragma solidity 0.8.28;

import {ERC20} from "@openzeppelin/contracts/token/ERC20/ERC20.sol";
import {ERC20Burnable} from "@openzeppelin/contracts/token/ERC20/extensions/ERC20Burnable.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";

/// @title VortexCoin
/// @notice The hotel's currency on its own chain: what the contest is counted in, and what a player
///         holds in the wallet the hotel derives for them.
///
/// @dev Deliberately an ERC-20 rather than the chain's native coin. The native coin needs no
///      contract at all and shows up in a wallet without being imported, which was the original
///      plan -- but a native coin cannot carry NFTs, and the two have to share a chain. One
///      contract each is the price of having both.
///
///      There is no cap and no fixed supply. A hotel currency is not a cryptocurrency with a
///      monetary policy: it is a scoreboard the operator writes to, exactly like credits and
///      duckets, and pretending otherwise by minting twenty-one million up front would only mean
///      the treasury holds a number nobody chose.
contract VortexCoin is ERC20, ERC20Burnable, Ownable {
    /// @param treasury The account that may mint, and that every award is sent from. In this hotel
    ///        that is a key the emulator holds; on a local chain it is anvil's first genesis
    ///        account.
    constructor(address treasury) ERC20("VortexCoin", "VTX") Ownable(treasury) {}

    /// @notice Creates `amount` for `to`. Only the treasury can call this, which is the whole of the
    ///         access control: a player's wallet can send and burn what it holds, and nothing else.
    function mint(address to, uint256 amount) external onlyOwner {
        _mint(to, amount);
    }

    /// @notice Mints to many accounts in one transaction -- what paying out a contest looks like.
    /// @dev Saves the per-transaction overhead of one send per winner, which on a chain with real
    ///      fees is the difference between a payout and a bill. The arrays must agree in length;
    ///      mismatched, this reverts rather than paying out the shorter one.
    function mintBatch(address[] calldata recipients, uint256[] calldata amounts)
        external
        onlyOwner
    {
        require(recipients.length == amounts.length, "VortexCoin: length mismatch");

        for (uint256 index = 0; index < recipients.length; index++) {
            _mint(recipients[index], amounts[index]);
        }
    }
}
