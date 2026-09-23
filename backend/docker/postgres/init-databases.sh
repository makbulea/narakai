#!/bin/bash
set -e

# One database per service. Sharing a database — even with separate schemas — makes it
# far too easy for one service to join across a boundary it should be calling over.
for db in customer_db product_db inventory_db order_db payment_db notification_db; do
  echo "Creating database $db"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-SQL
    CREATE DATABASE $db;
SQL
done
