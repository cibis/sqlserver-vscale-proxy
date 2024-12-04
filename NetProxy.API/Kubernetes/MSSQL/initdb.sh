sleep 90
echo "======================= INIT DB ======================= " & /opt/mssql-tools18/bin/sqlcmd -S localhost -l 60 -U SA -P "D<4KAgLJkD(v+8E333{;" -C -i initdb.sql & /opt/mssql/bin/sqlservr