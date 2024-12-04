#### In order to setup the kn cluster run below commands
```
minikube addons enable metrics-server

cd \
docker build -t mssql-proxy .

minikube image load mssql-proxy:latest

cd \Kubernetes\MSSQL\

docker build -t mssql .
```

#### to test in docker run: docker run -td --name mssql -p 4435:1433 mssql:latest
#### to load the docker image into minikube run
```
minikube image load mssql:latest

cd \Kubernetes\
```

#### Create the load balancing service
```
kubectl apply -f sqlloadbalancer.yaml
```

#### Create external storage with PV and PVC
```
kubectl apply -f sqlstorage-volume.yaml
kubectl apply -f sqlstorage-volume-claim.yaml

kubectl create secret generic mssql-secret --from-literal=SA_PASSWORD="D<4KAgLJkD(v+8E333{;"
```

#### Deploy the SQL Server 2019 container
```
kubectl apply -f sqldeployment.yaml
```

#### to enable the traffic to the sql proxy api running in the cluster. take note of the ports used for accessing from outside the cluter the api and sqlserver ports. 
#### Update ClusterAPIport and ClusterSQLServerPort values with the resulting port values before running KubernetesAPITests
```
minikube service mssql-proxy-service
```

#### use \Postman\kubernetes api calls.postman_collection.json to start the proxy in order to be able to connect to the db server running in te cluster