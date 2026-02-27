FROM node:20-alpine AS base
WORKDIR /app
EXPOSE 5000

FROM base AS deps
COPY src/Crypton.Web.Dashboard/package.json src/Crypton.Web.Dashboard/package-lock.json* ./
RUN npm ci

FROM base AS build
COPY --from=deps /app/node_modules ./node_modules
COPY src/Crypton.Web.Dashboard/ ./
RUN npm run build

FROM nginx:alpine AS final
COPY --from=build /app/dist /usr/share/nginx/html
COPY src/Crypton.Web.Dashboard/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 5000
CMD ["nginx", "-g", "daemon off;"]
