# WHY: One canonical manifest keeps fixture images and capability profiles aligned across all test assemblies.
# It is embedded as data; fixtures launch individual images, not a build of this multistage file.
FROM mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a AS mysql
FROM mariadb:11.8.9@sha256:79d59758afc91b89b120b0a8904d637f5a3b3e1c4900f29b740d6d46c72fef68 AS mariadb
FROM postgres:17.11-alpine3.24@sha256:f02121de6f74d30d8a94cd1d9584125e2178d7e6c377d8130112d4e52d867995 AS postgres
FROM mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04@sha256:2b5b581621126574f3d1f75e78d3eebe8d05aedb59ad0cfdf9aa42cb0634d726 AS sqlserver
