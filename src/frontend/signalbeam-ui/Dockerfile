# SignalBeam Platform Frontend
FROM node:20-alpine AS build

# Set working directory
WORKDIR /app

# Copy package files
COPY package*.json ./

# Install dependencies
RUN npm ci --only=production --silent

# Copy source code
COPY . .

# Build application
RUN npm run build

# Production stage - serve static files with simple HTTP server
FROM node:20-alpine AS final

# Install simple HTTP server
RUN npm install -g serve

# Create non-root user
RUN addgroup -g 1001 -S signalbeam && \
    adduser -S signalbeam -u 1001 -G signalbeam

# Create app directory and copy built files
WORKDIR /app
COPY --from=build /app/dist ./dist

# Set ownership
RUN chown -R signalbeam:signalbeam /app

# Switch to non-root user
USER signalbeam

# Expose port
EXPOSE 3000

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:3000/ || exit 1

# Start static file server (YARP will handle routing)
CMD ["serve", "-s", "dist", "-l", "3000"]