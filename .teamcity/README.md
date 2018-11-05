# TeamCity build notes

The following TeamCity build parameters are required by the scripts in this folder:  

## Plain-text

* _OAuthTokenUrl_: The UAA OAuth access token exchange url.
* _OAuthRedirectUrl_: The UAA OAuth redirect url. This should be the URL of the Dockerized functions + _/api/auth_.
* _DockerHub.Repo_: A Docker Hub (dockerhub.com) repo to host the image (nominally uitsssl/itpeople-functions).
* _DockerHub.Username_: A Docker Hub (dockerhub.com) account with access to the _DockerHub.Repo_.
* _DockerUcpBundle_: The name of the Docker UCP client bundle to source when authenticating to Docker UCP.
* *SPA_Host*: The Azure storage or CDN domain from which the React app assets will be served, e.g. https://SPA_Host/app/static/foo.js.
* *API_Host*: The domain of this Dockerized host, e.g. https://API_Host/api/ping.

## Secrets

* _OAuthClientId_: The UAA OAuth client ID.
* _OAuthClientSecret_: The UAA OAuth client secret.
* _JwtSecret_: The JWT signing secret. Any random 40-character alphanumeric string will do.
* _DbConnectionString_ : The PostgresQL connection string.
* _DockerHub.Username_: A Docker Hub password for the above username.

## Parameters Provided by TeamCity

* _vcsroot.branch_: The name of the branch from which this build originated, e.g. 'master', 'develop', 'feature/foo'.