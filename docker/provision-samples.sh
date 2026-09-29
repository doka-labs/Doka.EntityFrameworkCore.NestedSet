#!/usr/bin/env bash
# WHY: The same idempotent script runs during first initialization and explicitly against existing developer volumes.
(
    set -euo pipefail

    if [[ -n "${MARIADB_USER:-}" ]]; then
        sample_user="$MARIADB_USER"
        root_password="${MARIADB_ROOT_PASSWORD:?MARIADB_ROOT_PASSWORD is required}"
        client=mariadb
    elif [[ -n "${MYSQL_USER:-}" ]]; then
        sample_user="$MYSQL_USER"
        root_password="${MYSQL_ROOT_PASSWORD:?MYSQL_ROOT_PASSWORD is required}"
        client=mysql
    else
        printf '%s\n' 'Run this script inside the configured MySQL or MariaDB developer service.' >&2
        exit 1
    fi

    # WHY: Account names are configurable; quote their SQL literal without interpolating unescaped input.
    escaped_user="${sample_user//\\/\\\\}"
    escaped_user="${escaped_user//\'/\'\'}"

    # WHY: Underscores in GRANT database patterns are wildcards unless escaped, even inside backticks.
    MYSQL_PWD="$root_password" "$client" --protocol=socket --user=root --batch <<SQL
CREATE DATABASE IF NOT EXISTS nestedset_sample_filesystem CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
CREATE DATABASE IF NOT EXISTS nestedset_sample_kpis CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
CREATE DATABASE IF NOT EXISTS nestedset_sample_usergroups CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
GRANT ALL PRIVILEGES ON \`nestedset\\_sample\\_filesystem\`.* TO '$escaped_user'@'%';
GRANT ALL PRIVILEGES ON \`nestedset\\_sample\\_kpis\`.* TO '$escaped_user'@'%';
GRANT ALL PRIVILEGES ON \`nestedset\\_sample\\_usergroups\`.* TO '$escaped_user'@'%';
SQL

    printf '%s\n' 'The three sample databases and database-specific grants are ready. Existing data was preserved.'
)
