// =================================================================================================
// Trims forge's build output down to the two things anything outside this folder needs — the ABI and
// the deploy bytecode — and writes them to chain/artifacts, which IS committed.
//
//   node tools/chain/export-artifacts.mjs
//
// Two callers, and they are the reason this is committed rather than rebuilt:
//
//   - the deploy step, which is `cast send --create <bytecode>` and therefore needs no solc, no npm
//     and no forge-std in the container. The official foundry image alone can put these contracts on
//     a chain.
//   - the emulator, which encodes `mint`, `balanceOf` and `tokensOfOwner` calls against the ABI.
//
// forge's own artifacts under chain/out are 100 KB each of metadata, source maps and AST that
// neither caller reads, and they are gitignored because they must come from the pinned solc rather
// than from whatever a checkout happens to have compiled.
// =================================================================================================

import {mkdirSync, readFileSync, writeFileSync} from 'node:fs';
import {dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';

const CHAIN = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'chain');
const CONTRACTS = ['VortexCoin', 'VortexCollectible'];

mkdirSync(join(CHAIN, 'artifacts'), {recursive: true});

for(const name of CONTRACTS)
{
    const source = join(CHAIN, 'out', `${name}.sol`, `${name}.json`);
    let built;

    try
    {
        built = JSON.parse(readFileSync(source, 'utf8'));
    }
    catch
    {
        console.error(`${name}: no build output at ${source}. Run \`forge build\` in chain/ first.`);
        process.exitCode = 1;

        continue;
    }

    // `bytecode.object` is the CREATION code — the constructor plus the runtime code it returns.
    // `deployedBytecode` is only what ends up stored at the address and cannot be deployed, which is
    // the mistake that produces a transaction that succeeds and leaves an empty account behind.
    const bytecode = built.bytecode?.object;

    if(!bytecode || bytecode === '0x')
    {
        console.error(`${name}: build output carries no creation bytecode.`);
        process.exitCode = 1;

        continue;
    }

    writeFileSync(
        join(CHAIN, 'artifacts', `${name}.json`),
        JSON.stringify({contract: name, abi: built.abi, bytecode}, null, 2) + '\n'
    );

    // The same bytecode again, on its own, because the deploy step runs inside the official foundry
    // image and that image carries the foundry binaries and very little else — no jq, no node. A
    // bare .hex file is something `cat` can read, and it is the difference between a deploy script
    // of one line and one that greps JSON with sed.
    writeFileSync(join(CHAIN, 'artifacts', `${name}.hex`), bytecode + '\n');

    console.log(`${name}: ${built.abi.length} ABI entries, ${(bytecode.length - 2) / 2} bytes of creation code`);
}
