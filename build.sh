#!/bin/sh
DIRS="scripts/ImportOrgData openapi/Swashbuckle.AspNetCore.AzureFunctions openapi/Swashbuckle.AspNetCore.Filters functions database functions.tests.unit functions.tests.stateserver functions.tests.integration"
for dir in $DIRS; do
	rm -rf $dir/bin $dir/obj
done
for dir in $DIRS; do
	dotnet build $dir
done