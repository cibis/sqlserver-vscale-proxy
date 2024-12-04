FROM mcr.microsoft.com/mssql/server:2022-latest
ENV ACCEPT_EULA y
ENV SA_PASSWORD "D<4KAgLJkD(v+8E333{;"
COPY ./initdb.sql .
COPY ./initdb.sh .
EXPOSE 1433
CMD /bin/bash ./initdb.sh