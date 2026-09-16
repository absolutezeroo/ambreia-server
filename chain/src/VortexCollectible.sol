// SPDX-License-Identifier: MIT
pragma solidity 0.8.28;

import {ERC721} from "@openzeppelin/contracts/token/ERC721/ERC721.sol";
import {ERC721Burnable} from "@openzeppelin/contracts/token/ERC721/extensions/ERC721Burnable.sol";
import {ERC721Enumerable} from
    "@openzeppelin/contracts/token/ERC721/extensions/ERC721Enumerable.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";

/// @title VortexCollectible
/// @notice The hotel's Relics as real NFTs -- the on-chain half of what `NftAssetEntity` is in the
///         database, and what the Collectors Guild's transfer door moves in and out.
///
/// @dev The token id IS the hotel's own asset id. Nothing here allocates ids of its own, because two
///      independent numberings of the same object is the bug that bridges are made of: a Relic that
///      is asset 91 in the hotel and token 4 on the chain needs a mapping table, and that table is
///      one more thing that can disagree with itself.
///
///      ERC721Enumerable is carried on purpose and is not decoration. It is what answers "what does
///      this wallet hold" in two `eth_call`s -- `balanceOf` then `tokenOfOwnerByIndex` -- instead of
///      requiring an indexer that follows Transfer logs, keeps its own database and has to be told
///      what to do about a reorg. That is an entire component this hotel now does not need. It costs
///      gas on every mint and transfer, which on a chain the hotel runs itself is not a cost.
///
///      Transfers are NOT restricted. A player who has moved a Relic to their own wallet owns it in
///      the sense the word means on a chain, and can send it to anyone; that is the whole point of
///      the door existing, and a soulbound token would have been an expensive way to describe a
///      database row.
contract VortexCollectible is ERC721, ERC721Enumerable, ERC721Burnable, Ownable {
    /// @dev Prefixed to the token id by `tokenURI`. Settable rather than frozen into the bytecode:
    ///      a hotel that changes domain would otherwise have every Relic pointing at metadata that
    ///      no longer answers, with no way to fix it short of redeploying and reminting.
    string private _baseTokenURI;

    event BaseURIChanged(string baseURI);

    /// @param treasury The account that may mint and set the base URI -- the emulator's own key.
    /// @param baseURI Where metadata lives, e.g. `https://hotel.example/api/public/collectibles/`.
    constructor(address treasury, string memory baseURI)
        ERC721("Vortex Collectible", "VRELIC")
        Ownable(treasury)
    {
        _baseTokenURI = baseURI;
    }

    /// @notice Mints the Relic whose hotel asset id is `assetId` to `to`.
    /// @dev Reverts if that id already exists, which is `ERC721._mint`'s own behaviour and exactly
    ///      the guard wanted: minting the same asset twice would put two chain tokens against one
    ///      database row.
    function mint(address to, uint256 assetId) external onlyOwner {
        _safeMint(to, assetId);
    }

    /// @notice Mints many Relics in one transaction -- handing over a whole collection at once.
    function mintBatch(address to, uint256[] calldata assetIds) external onlyOwner {
        for (uint256 index = 0; index < assetIds.length; index++) {
            _safeMint(to, assetIds[index]);
        }
    }

    function setBaseURI(string calldata baseURI) external onlyOwner {
        _baseTokenURI = baseURI;

        emit BaseURIChanged(baseURI);
    }

    /// @notice Every token id `owner` holds, in one call.
    /// @dev A convenience over `ERC721Enumerable`, not a replacement for it: this is what the
    ///      emulator actually wants to ask, and asking it as one `eth_call` rather than as
    ///      `balanceOf` followed by N round trips is the difference between reading a wallet in one
    ///      request and in fifty. Unbounded, so it is a read-only call and never a transaction.
    function tokensOfOwner(address owner) external view returns (uint256[] memory) {
        uint256 held = balanceOf(owner);
        uint256[] memory ids = new uint256[](held);

        for (uint256 index = 0; index < held; index++) {
            ids[index] = tokenOfOwnerByIndex(owner, index);
        }

        return ids;
    }

    function _baseURI() internal view override returns (string memory) {
        return _baseTokenURI;
    }

    // --- The two overrides multiple inheritance forces, and nothing else -------------------------
    //
    // ERC721 and ERC721Enumerable both define these, so Solidity requires the resolution to be
    // written out. Both simply defer to `super`, which walks the linearised chain and reaches the
    // Enumerable bookkeeping; getting this wrong is how an Enumerable token ends up with indexes
    // that do not match its owners.

    function _update(address to, uint256 tokenId, address auth)
        internal
        override(ERC721, ERC721Enumerable)
        returns (address)
    {
        return super._update(to, tokenId, auth);
    }

    function _increaseBalance(address account, uint128 value)
        internal
        override(ERC721, ERC721Enumerable)
    {
        super._increaseBalance(account, value);
    }

    function supportsInterface(bytes4 interfaceId)
        public
        view
        override(ERC721, ERC721Enumerable)
        returns (bool)
    {
        return super.supportsInterface(interfaceId);
    }
}
