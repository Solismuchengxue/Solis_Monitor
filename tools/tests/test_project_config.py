import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2] / "firmware"


class ProjectConfigTests(unittest.TestCase):
    def test_converged_repository_layout_has_expected_entrypoints(self):
        repository_root = PROJECT_ROOT.parent

        self.assertTrue((PROJECT_ROOT / "CMakeLists.txt").is_file())
        self.assertTrue((PROJECT_ROOT / "main" / "app_main.c").is_file())
        self.assertTrue(
            (
                repository_root
                / "app"
                / "LibreHardwareMonitor"
                / "LibreHardwareMonitor"
                / "Program.cs"
            ).is_file()
        )
        self.assertTrue((repository_root / "reference" / "assets" / "background.png").is_file())
        self.assertFalse((repository_root / "pc_app").exists())
        self.assertFalse((repository_root / "components").exists())
        self.assertFalse((repository_root / "main").exists())
        self.assertFalse((repository_root / "old").exists())

    def test_desktop_build_matrix_matches_product_and_upstream_boundaries(self):
        repository_root = PROJECT_ROOT.parent
        desktop_root = repository_root / "app" / "LibreHardwareMonitor"

        def properties(project_name):
            project = ET.parse(desktop_root / project_name).getroot()
            return {
                element.tag: element.text
                for group in project.findall("PropertyGroup")
                if "Condition" not in group.attrib
                for element in group
            }

        desktop = properties("LibreHardwareMonitor/LibreHardwareMonitor.csproj")
        aga_controls = properties("Aga.Controls/Aga.Controls.csproj")
        hardware_library = properties(
            "LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj"
        )

        self.assertEqual(desktop.get("TargetFramework"), "net10.0-windows")
        self.assertNotIn("TargetFrameworks", desktop)
        self.assertEqual(desktop.get("Platforms"), "x64")
        self.assertEqual(desktop.get("RuntimeIdentifiers"), "win-x64")
        desktop_project = ET.parse(
            desktop_root / "LibreHardwareMonitor/LibreHardwareMonitor.csproj"
        ).getroot()
        self.assertEqual(desktop_project.attrib.get("Sdk"), "Microsoft.NET.Sdk")
        desktop_packages = {
            item.attrib["Include"]
            for item in desktop_project.findall(".//PackageReference")
        }
        self.assertNotIn("System.Resources.Extensions", desktop_packages)
        self.assertNotIn("System.Text.Json", desktop_packages)

        self.assertEqual(aga_controls.get("TargetFramework"), "net472")
        self.assertEqual(aga_controls.get("Platforms"), "x64;x86;ARM64")

        self.assertEqual(
            hardware_library.get("TargetFrameworks"),
            "net472;netstandard2.0;net8.0;net9.0;net10.0",
        )
        self.assertEqual(hardware_library.get("Platforms"), "x64;x86;ARM64")
        self.assertEqual(
            hardware_library.get("RuntimeIdentifiers"),
            "win-x64;win-x86;win-arm64",
        )

    def test_clean_build_defaults_to_esp32s3(self):
        defaults = (PROJECT_ROOT / "sdkconfig.defaults").read_text(encoding="utf-8")
        self.assertIn('CONFIG_IDF_TARGET="esp32s3"', defaults.splitlines())

    def test_installer_closes_tray_process_before_uninstalling_files(self):
        installer = (
            PROJECT_ROOT.parent / "installer" / "SolisMonitor.iss"
        ).read_text(encoding="utf-8")

        self.assertIn("CloseApplications=force", installer)
        uninstall_run = installer.split("[UninstallRun]", maxsplit=1)[1]
        stop_process = (
            'Filename: "{sys}\\taskkill.exe"; '
            'Parameters: "/IM SolisMonitor.exe /T /F"'
        )
        self.assertIn(stop_process, uninstall_run)
        self.assertLess(
            uninstall_run.index(stop_process),
            uninstall_run.index("SolisMonitor.NotificationHost.exe"),
        )

    def test_verify_script_runs_all_checks_with_single_build_concurrency(self):
        verify_script = (
            PROJECT_ROOT.parent / "tools" / "verify.ps1"
        ).read_text(encoding="utf-8")

        self.assertIn("--disable-parallel", verify_script)
        self.assertGreaterEqual(verify_script.count("-m:1"), 2)
        self.assertGreaterEqual(
            verify_script.count("-p:BuildInParallel=false"),
            2,
        )
        self.assertIn("--no-build --no-restore", verify_script)
        self.assertIn(
            "-m unittest discover -s tools\\tests -p \"test_*.py\" -v",
            verify_script,
        )
        self.assertIn(" reconfigure", verify_script)
        self.assertIn("ninja -C $firmwareBuild -j1", verify_script)

    def test_github_actions_ci_covers_desktop_and_tooling_checks(self):
        workflow = (
            PROJECT_ROOT.parent / ".github" / "workflows" / "ci.yml"
        ).read_text(encoding="utf-8")

        self.assertIn("windows-latest", workflow)
        self.assertIn("actions/checkout@v7", workflow)
        self.assertIn("actions/setup-dotnet@v6", workflow)
        self.assertIn("dotnet-version: '10.0.x'", workflow)
        self.assertIn("actions/setup-python@v7", workflow)
        self.assertIn("python-version: '3.12'", workflow)
        self.assertIn("permissions:\n  contents: read", workflow)
        self.assertIn("timeout-minutes:", workflow)
        self.assertIn("dotnet restore", workflow)
        notification_host_restore = (
            "dotnet restore .\\app\\LibreHardwareMonitor\\"
            "SolisMonitor.NotificationHost\\SolisMonitor.NotificationHost.csproj"
        )
        desktop_build = (
            "dotnet build .\\app\\LibreHardwareMonitor\\"
            "LibreHardwareMonitor\\LibreHardwareMonitor.csproj"
        )
        self.assertIn(notification_host_restore, workflow)
        self.assertLess(
            workflow.index(notification_host_restore),
            workflow.index(desktop_build),
        )
        self.assertGreaterEqual(workflow.count("dotnet build"), 2)
        self.assertIn("dotnet run --project", workflow)
        self.assertIn("python -m unittest discover", workflow)
        self.assertNotIn("idf.py", workflow)

    def test_network_client_internal_header_is_private_but_unit_testable(self):
        component = PROJECT_ROOT / "components" / "network_client"
        component_cmake = (component / "CMakeLists.txt").read_text(encoding="utf-8")
        unit_cmake = (PROJECT_ROOT / "test_apps" / "unit" / "main" / "CMakeLists.txt").read_text(
            encoding="utf-8"
        )

        self.assertTrue((component / "private_include" / "network_client_internal.h").is_file())
        self.assertFalse((component / "network_client_internal.h").exists())
        self.assertIn('INCLUDE_DIRS "include"', component_cmake)
        self.assertIn('PRIV_INCLUDE_DIRS "private_include"', component_cmake)
        self.assertNotIn('PRIV_INCLUDE_DIRS "../../../components/network_client', unit_cmake)
        self.assertIn(
            'target_include_directories(${COMPONENT_LIB} PRIVATE '
            '"../../../components/network_client/private_include")',
            unit_cmake,
        )

    def test_unit_ota_compatibility_is_explicit_and_default_off(self):
        unit_cmake = (
            PROJECT_ROOT / "test_apps" / "unit" / "CMakeLists.txt"
        ).read_text(encoding="utf-8")

        self.assertIn('option(SOLIS_UNIT_OTA_COMPAT ', unit_cmake)
        self.assertIn("if(SOLIS_UNIT_OTA_COMPAT)", unit_cmake)
        self.assertIn("project(solis_monitor)", unit_cmake)
        self.assertIn("project(solis_monitor_unit)", unit_cmake)

    def test_serial_setup_uses_blocking_console_uart_driver(self):
        component = PROJECT_ROOT / "components" / "serial_setup"
        component_cmake = (component / "CMakeLists.txt").read_text(encoding="utf-8")
        source = (component / "serial_setup.c").read_text(encoding="utf-8")

        self.assertIn("esp_driver_uart", component_cmake)
        self.assertIn('#include "driver/uart.h"', source)
        self.assertIn('#include "driver/uart_vfs.h"', source)
        self.assertIn(
            "uart_driver_install(CONFIG_ESP_CONSOLE_UART_NUM, "
            "SERIAL_SETUP_UART_RX_BUFFER_SIZE, 0, 0, NULL, 0)",
            source,
        )
        self.assertIn("uart_vfs_dev_use_driver(CONFIG_ESP_CONSOLE_UART_NUM)", source)
        self.assertLess(
            source.index("uart_driver_install(CONFIG_ESP_CONSOLE_UART_NUM"),
            source.index("uart_vfs_dev_use_driver(CONFIG_ESP_CONSOLE_UART_NUM)"),
        )
        self.assertLess(
            source.index("uart_vfs_dev_use_driver(CONFIG_ESP_CONSOLE_UART_NUM)"),
            source.index("xTaskCreate(serial_setup_task"),
        )
        task_create = source.index("xTaskCreate(serial_setup_task")
        self.assertIn("uart_vfs_dev_use_nonblocking(CONFIG_ESP_CONSOLE_UART_NUM)", source)
        self.assertIn("uart_driver_delete(CONFIG_ESP_CONSOLE_UART_NUM)", source)
        restore_nonblocking = source.index(
            "uart_vfs_dev_use_nonblocking(CONFIG_ESP_CONSOLE_UART_NUM)", task_create
        )
        delete_driver = source.index("uart_driver_delete(CONFIG_ESP_CONSOLE_UART_NUM)", task_create)
        self.assertLess(task_create, restore_nonblocking)
        self.assertLess(restore_nonblocking, delete_driver)


if __name__ == "__main__":
    unittest.main()
