FROM node:20-alpine AS base
WORKDIR /app
EXPOSE 3000

FROM base AS deps
COPY src/monitoring_dashboard/frontend/package.json src/monitoring_dashboard/frontend/package-lock.json* ./
RUN npm ci

FROM base AS build
COPY --from=deps /app/node_modules ./node_modules
COPY src/monitoring_dashboard/frontend/ ./
RUN npm run build

FROM nginx:alpine AS final
COPY --from=build /app/dist /usr/share/nginx/html
COPY src/monitoring_dashboard/frontend/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
