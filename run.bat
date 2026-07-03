@echo off
echo ==============================================
echo 📊 Starting Flint Chart Agent Application...
echo ==============================================

echo ⏳ Starting C# AGUI Backend on port 5000...
start "Flint Backend" cmd /c "dotnet run --urls http://localhost:5000"

echo ⏳ Starting Next.js Frontend on port 3000...
start "Flint Frontend" cmd /c "cd frontend && npm run dev"

echo ✅ Both services are launching in separate windows!
echo - Backend: http://localhost:5000/agent
echo - Frontend: http://localhost:3000
echo ==============================================
pause
