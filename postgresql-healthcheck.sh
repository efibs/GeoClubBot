#!/bin/bash

# Read the username from the secret file
USERNAME=$(cat /run/secrets/postgresqldb-username)

# Run the normal Postgres readiness check
pg_isready -U "$USERNAME"