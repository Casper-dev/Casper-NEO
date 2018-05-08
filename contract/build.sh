#!/bin/sh

dotnet publish -o testlib
cd testlib \
    && dotnet neon.dll neo-sc.dll \
    && cp neo-sc.avm ~/repo/neo-python/casper.avm \
    && echo success
#    && sudo docker cp `pwd`/neo-sc.avm neo-python:/neo-python/casper.avm
