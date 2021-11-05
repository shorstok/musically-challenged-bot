docker-compose build
docker-compose -p muschallenge-dev -f docker-compose.yml -f docker-compose.dev.yml up --force-recreate -d