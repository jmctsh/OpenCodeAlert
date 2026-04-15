import sys

with open("OpenRA.sln", "r") as f:
    content = f.read()

new_proj = """Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "OpenRA.Platforms.Headless", "OpenRA.Platforms.Headless\\OpenRA.Platforms.Headless.csproj", "{44D03738-C154-4028-8EA8-63A3C488A651}"
EndProject
"""

if "OpenRA.Platforms.Headless" not in content:
    content = content.replace("Global\n", new_proj + "Global\n")
    
    cfg1 = "                {44D03738-C154-4028-8EA8-63A3C488A651}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\n"
    cfg2 = "                {44D03738-C154-4028-8EA8-63A3C488A651}.Debug|Any CPU.Build.0 = Debug|Any CPU\n"
    cfg3 = "                {44D03738-C154-4028-8EA8-63A3C488A651}.Release|Any CPU.ActiveCfg = Release|Any CPU\n"
    cfg4 = "                {44D03738-C154-4028-8EA8-63A3C488A651}.Release|Any CPU.Build.0 = Release|Any CPU\n"
    
    content = content.replace("EndGlobalSection\n        GlobalSection(SolutionProperties)", cfg1 + cfg2 + cfg3 + cfg4 + "        EndGlobalSection\n        GlobalSection(SolutionProperties)")

with open("OpenRA.sln", "w") as f:
    f.write(content)
