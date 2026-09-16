// SPDX-License-Identifier: MIT
pragma solidity 0.8.28;

import {Test} from "forge-std/Test.sol";
import {VortexCoin} from "../src/VortexCoin.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";

/// @notice What the contest rests on: only the treasury creates VTX, everyone can move what they
///         hold, and a batch payout either pays everyone or nobody.
contract VortexCoinTest is Test {
    VortexCoin private coin;

    address private constant TREASURY = address(0xA11CE);
    address private constant PLAYER = address(0xB0B);
    address private constant OTHER = address(0xCAFE);

    function setUp() public {
        coin = new VortexCoin(TREASURY);
    }

    function test_treasury_owns_the_contract() public view {
        assertEq(coin.owner(), TREASURY);
        assertEq(coin.name(), "VortexCoin");
        assertEq(coin.symbol(), "VTX");
        assertEq(coin.decimals(), 18);
        assertEq(coin.totalSupply(), 0);
    }

    function test_treasury_can_mint() public {
        vm.prank(TREASURY);
        coin.mint(PLAYER, 250 ether);

        assertEq(coin.balanceOf(PLAYER), 250 ether);
        assertEq(coin.totalSupply(), 250 ether);
    }

    /// @dev The one that matters. Anybody being able to mint is not a bug in a contest, it is the
    ///      end of the contest.
    function test_nobody_else_can_mint() public {
        vm.prank(PLAYER);
        vm.expectRevert(
            abi.encodeWithSelector(Ownable.OwnableUnauthorizedAccount.selector, PLAYER)
        );
        coin.mint(PLAYER, 1 ether);
    }

    function test_mintBatch_pays_everyone() public {
        address[] memory winners = new address[](2);
        uint256[] memory amounts = new uint256[](2);

        winners[0] = PLAYER;
        winners[1] = OTHER;
        amounts[0] = 100 ether;
        amounts[1] = 50 ether;

        vm.prank(TREASURY);
        coin.mintBatch(winners, amounts);

        assertEq(coin.balanceOf(PLAYER), 100 ether);
        assertEq(coin.balanceOf(OTHER), 50 ether);
    }

    /// @dev Mismatched arrays revert rather than paying out the shorter one, so a payout built from
    ///      two lists that drifted apart fails loudly instead of quietly shortchanging the tail.
    function test_mintBatch_refuses_mismatched_lengths() public {
        address[] memory winners = new address[](2);
        uint256[] memory amounts = new uint256[](1);

        winners[0] = PLAYER;
        winners[1] = OTHER;
        amounts[0] = 100 ether;

        vm.prank(TREASURY);
        vm.expectRevert("VortexCoin: length mismatch");
        coin.mintBatch(winners, amounts);

        assertEq(coin.totalSupply(), 0);
    }

    function test_a_player_can_send_what_they_hold() public {
        vm.prank(TREASURY);
        coin.mint(PLAYER, 250 ether);

        vm.prank(PLAYER);
        coin.transfer(OTHER, 40 ether);

        assertEq(coin.balanceOf(PLAYER), 210 ether);
        assertEq(coin.balanceOf(OTHER), 40 ether);
    }

    function test_a_player_cannot_send_what_they_do_not_hold() public {
        vm.prank(PLAYER);
        vm.expectRevert();
        coin.transfer(OTHER, 1 ether);
    }

    function test_burning_reduces_the_supply() public {
        vm.prank(TREASURY);
        coin.mint(PLAYER, 250 ether);

        vm.prank(PLAYER);
        coin.burn(50 ether);

        assertEq(coin.balanceOf(PLAYER), 200 ether);
        assertEq(coin.totalSupply(), 200 ether);
    }
}
