#!/usr/bin/env bash
# Copy-only backup of the target database before a deploy (SCRUM-313 / #295). Run by deploy-site.yml; also usable by hand.
# The backup file is written by SQL Server itself, so BACKUP_DIR is a folder on the DATABASE server that its service account may write
# (secret <P>_BACKUP_DIR). The connection string is read from the environment (CONN), never from the command line, and nothing secret is printed.
# Exit codes: 0 = backed up, or skipped on purpose (BACKUP_DIR empty: a warning is printed), 1 = a configured backup failed (the deploy must stop).
# Restore: docs/deployment/DEPLOYMENT_README.md "Roll back" / the runbook.
set -uo pipefail

dir="${BACKUP_DIR:-}"
if [ -z "$dir" ]; then
  echo "::warning::No <PREFIX>_BACKUP_DIR secret: the database is NOT backed up by this pipeline. Take a Plesk backup or a manual backup before relying on a rollback."
  exit 0
fi
: "${CONN:?CONN (connection string) is required}"

field() { # name -> value of "name=value;" in the connection string, case-insensitive
  printf '%s' "$CONN" | tr ';' '\n' | sed -n -E "s/^[[:space:]]*($1)[[:space:]]*=[[:space:]]*//Ip" | head -1
}
server=$(field 'Server|Data Source|Address')
db=$(field 'Database|Initial Catalog')
user=$(field 'User Id|UID|User')
pass=$(field 'Password|PWD')
if [ -z "$server" ] || [ -z "$db" ] || [ -z "$user" ] || [ -z "$pass" ]; then
  echo "::error::The connection string has no Server, Database, User Id or Password; the backup cannot run."; exit 1
fi
case "$db" in *[!A-Za-z0-9_.-]*) echo "::error::Unexpected characters in the database name."; exit 1;; esac
case "$dir" in *"'"*) echo "::error::BACKUP_DIR must not contain a quote."; exit 1;; esac

if ! command -v sqlcmd > /dev/null 2>&1; then
  for p in /opt/mssql-tools18/bin /opt/mssql-tools/bin; do [ -x "$p/sqlcmd" ] && export PATH="$PATH:$p"; done
fi
if ! command -v sqlcmd > /dev/null 2>&1; then
  echo "sqlcmd not found on the runner; installing mssql-tools18"
  sudo ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev > /dev/null 2>&1 && export PATH="$PATH:/opt/mssql-tools18/bin"
fi
command -v sqlcmd > /dev/null 2>&1 || { echo "::error::sqlcmd is not available; the configured backup cannot run."; exit 1; }

stamp=$(date -u +%Y%m%dT%H%M%SZ)
file="${dir%/}/${db}-${stamp}.bak"
echo "Backing up database ${db} (copy-only) to ${file} on the database server"
export SQLCMDPASSWORD="$pass"
if sqlcmd -S "$server" -U "$user" -C -b -l 60 -Q "BACKUP DATABASE [${db}] TO DISK = N'${file}' WITH COPY_ONLY, INIT, CHECKSUM, COMPRESSION, NAME = N'pems-pre-deploy-${stamp}'" > backup.out 2>&1; then
  echo "Backup finished: ${file}"
  [ -n "${GITHUB_STEP_SUMMARY:-}" ] && echo "Database backup before this deploy: \`${file}\` (on the database server)" >> "$GITHUB_STEP_SUMMARY"
  exit 0
fi
echo "::error::The database backup failed, so the deploy stops: $(tr '\n' ' ' < backup.out | head -c 400). Check that the database user may run BACKUP and that the SQL Server service account may write to the folder (or clear <PREFIX>_BACKUP_DIR to deploy without a backup)."
exit 1
