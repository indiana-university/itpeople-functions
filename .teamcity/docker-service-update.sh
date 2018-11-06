# Source the Docker client bundle for the environment associated with this build.
source $HOME/.dcd/%DockerUcp.Bundle%.sh

# Update the service and non-secret environment variables
docker service update --image %DockerHub.Repo%:%teamcity.build.branch% itpeople-functions