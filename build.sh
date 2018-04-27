#!/bin/sh

dotnet publish -o testlib
cd testlib \
    && dotnet neon.dll neo-sc.dll \
    && cp neo-sc.avm ~/repo/neo-python/casper.avm \
