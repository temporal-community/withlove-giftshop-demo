FROM mcr.microsoft.com/mssql/server:2025-latest

USER root

RUN export DEBIAN_FRONTEND=noninteractive && \
    apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates curl && \
    curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/mssql-server-2025.list \
        | tee /etc/apt/sources.list.d/mssql-server.list >/dev/null && \
    apt-get update && \
    apt-get install -y --no-install-recommends mssql-server-fts && \
    apt-get purge -y --auto-remove curl && \
    rm -rf /var/lib/apt/lists/*

USER mssql
