# kraken_integration

## Run with docker:

docker build -t kraken_integration_image -f docker/Dockerfile .

docker run -it --rm kraken_integration_image

## Description

This is a c# demo. Running the Cli (see above) connects (via websocket) to the cryptocurrency exchange "kraken" (at kraken.com), then tracks the top-10 orderbook levels of the pairs XBT/USDT and ETH/USDT (Bitcoin/US dollar and Ethereum/US dollar).

The demo is missing logging. Also, the testsuite is incomplete, and is particularly missing several integration tests. 

This demo touches upon a range of c# concepts, including: Tasks, cancellation, lock, System.Collections.Concurrent,  System.Collections.Immutable, Linq, Reactive extensions, abstract base classes & inheritance as well as interfaces.

A optimization that would likely improve performance would be extensive use of ref readonly structs, a concept which is sparsely utilized currently.

Notably, a extensible, vendor-flexible and highly testable codebase is achieved through extensive use of the denpendency injection pattern.


