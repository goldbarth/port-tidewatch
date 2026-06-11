# Build context: frontend/
#   docker build -f frontend/Dockerfile -t tidewatch-dashboard:dev frontend
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# Static SPA only. /api routing is handled by the cluster Ingress, not nginx here.
FROM nginx:alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/tidewatch-dashboard/browser /usr/share/nginx/html
EXPOSE 80
