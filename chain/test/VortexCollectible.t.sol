// SPDX-License-Identifier: MIT
pragma solidity 0.8.28;

import {Test} from "forge-std/Test.sol";
import {VortexCollectible} from "../src/VortexCollectible.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";

/// @notice What the Relic bridge rests on: the token id is the hotel's own asset id and can never be
///         minted twice, the hotel alone mints, and a wallet's holdings can be read back without an
///         indexer.
contract VortexCollectibleTest is Test {
    VortexCollectible private relics;

    address private constant TREASURY = address(0xA11CE);
    address private constant PLAYER = address(0xB0B);
    address private constant OTHER = address(0xCAFE);

    string private constant BASE_URI = "https://hotel.example/api/public/collectibles/";

    /// @dev Real-looking asset ids rather than 1, 2, 3: the contract is told the hotel's numbering
    ///      and never invents one, so a test that uses sequential ids would pass just as well
    ///      against an implementation that counted up on its own.
    uint256 private constant ASSET_A = 90714;
    uint256 private constant ASSET_B = 90715;

    function setUp() public {
        relics = new VortexCollectible(TREASURY, BASE_URI);
    }

    function test_treasury_owns_the_contract() public view {
        assertEq(relics.owner(), TREASURY);
        assertEq(relics.name(), "Vortex Collectible");
        assertEq(relics.symbol(), "VRELIC");
        assertEq(relics.totalSupply(), 0);
    }

    function test_the_token_id_is_the_hotel_asset_id() public {
        vm.prank(TREASURY);
        relics.mint(PLAYER, ASSET_A);

        assertEq(relics.ownerOf(ASSET_A), PLAYER);
        assertEq(relics.tokenURI(ASSET_A), string.concat(BASE_URI, "90714"));
    }

    /// @dev The guard that keeps one database row from becoming two chain tokens.
    function test_the_same_asset_cannot_be_minted_twice() public {
        vm.startPrank(TREASURY);
        relics.mint(PLAYER, ASSET_A);

        vm.expectRevert();
        relics.mint(OTHER, ASSET_A);
        vm.stopPrank();

        assertEq(relics.ownerOf(ASSET_A), PLAYER);
        assertEq(relics.totalSupply(), 1);
    }

    function test_nobody_else_can_mint() public {
        vm.prank(PLAYER);
        vm.expectRevert(
            abi.encodeWithSelector(Ownable.OwnableUnauthorizedAccount.selector, PLAYER)
        );
        relics.mint(PLAYER, ASSET_A);
    }

    /// @dev The reason ERC721Enumerable is carried: this is the whole of "what does this wallet
    ///      hold", and it is a read, not an indexer.
    function test_a_wallet_can_be_read_back_in_one_call() public {
        uint256[] memory minted = new uint256[](2);
        minted[0] = ASSET_A;
        minted[1] = ASSET_B;

        vm.prank(TREASURY);
        relics.mintBatch(PLAYER, minted);

        uint256[] memory held = relics.tokensOfOwner(PLAYER);

        assertEq(held.length, 2);
        assertEq(held[0], ASSET_A);
        assertEq(held[1], ASSET_B);
        assertEq(relics.tokensOfOwner(OTHER).length, 0);
    }

    /// @dev Transfers are open, and the enumeration has to follow them — an index left pointing at
    ///      the previous owner is the classic way an Enumerable token goes wrong.
    function test_a_player_can_send_a_relic_and_the_enumeration_follows() public {
        uint256[] memory minted = new uint256[](2);
        minted[0] = ASSET_A;
        minted[1] = ASSET_B;

        vm.prank(TREASURY);
        relics.mintBatch(PLAYER, minted);

        vm.prank(PLAYER);
        relics.transferFrom(PLAYER, OTHER, ASSET_A);

        assertEq(relics.ownerOf(ASSET_A), OTHER);

        uint256[] memory left = relics.tokensOfOwner(PLAYER);
        uint256[] memory gained = relics.tokensOfOwner(OTHER);

        assertEq(left.length, 1);
        assertEq(left[0], ASSET_B);
        assertEq(gained.length, 1);
        assertEq(gained[0], ASSET_A);
    }

    function test_a_stranger_cannot_move_someone_elses_relic() public {
        vm.prank(TREASURY);
        relics.mint(PLAYER, ASSET_A);

        vm.prank(OTHER);
        vm.expectRevert();
        relics.transferFrom(PLAYER, OTHER, ASSET_A);
    }

    function test_burning_removes_it_from_the_wallet() public {
        vm.prank(TREASURY);
        relics.mint(PLAYER, ASSET_A);

        vm.prank(PLAYER);
        relics.burn(ASSET_A);

        assertEq(relics.totalSupply(), 0);
        assertEq(relics.tokensOfOwner(PLAYER).length, 0);
    }

    /// @dev The base URI is settable so a hotel that changes domain does not strand the metadata of
    ///      every Relic ever minted.
    function test_the_base_uri_can_be_moved() public {
        vm.prank(TREASURY);
        relics.mint(PLAYER, ASSET_A);

        vm.prank(TREASURY);
        relics.setBaseURI("https://moved.example/c/");

        assertEq(relics.tokenURI(ASSET_A), "https://moved.example/c/90714");
    }

    function test_nobody_else_can_move_the_base_uri() public {
        vm.prank(PLAYER);
        vm.expectRevert(
            abi.encodeWithSelector(Ownable.OwnableUnauthorizedAccount.selector, PLAYER)
        );
        relics.setBaseURI("https://attacker.example/");
    }
}
